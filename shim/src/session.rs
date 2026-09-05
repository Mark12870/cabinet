use crate::x11;
use std::ffi::{OsStr, OsString};
use std::fs::File;
use std::io::{self, Read, Write};
use std::os::raw::{c_int, c_long, c_short, c_void};
use std::os::unix::ffi::{OsStrExt, OsStringExt};
use std::os::unix::io::{AsRawFd, FromRawFd, RawFd};
use std::os::unix::net::{UnixListener, UnixStream};
use std::os::unix::process::{CommandExt, ExitStatusExt};
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::ptr;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::thread;
use std::time::{Duration, Instant};

const IDLE_GRACE: Duration = Duration::from_secs(10);
const START_TIMEOUT: Duration = Duration::from_secs(30);
const TICK: Duration = Duration::from_millis(20);
const ATTEMPTS: u32 = 3;

pub fn key(seed: &OsStr) -> String {
    let mut hash: u64 = 0xcbf2_9ce4_8422_2325;

    for byte in seed.as_bytes() {
        hash ^= u64::from(*byte);
        hash = hash.wrapping_mul(0x0000_0100_0000_01b3);
    }

    format!("cabinet-{hash:016x}")
}

pub fn socket_path(dir: &Path, key: &str) -> PathBuf {
    dir.join(format!("{key}.sock"))
}

pub fn lock_path(dir: &Path, key: &str) -> PathBuf {
    dir.join(format!("{key}.lock"))
}

pub fn busy_path(dir: &Path, key: &str) -> PathBuf {
    dir.join(format!("{key}.busy"))
}

pub fn live(socket: &Path) -> bool {
    UnixStream::connect(socket).is_ok()
}

pub struct Busy(File);

fn whole_file(kind: c_short) -> FileLock {
    FileLock {
        kind,
        whence: SEEK_SET,
        start: 0,
        len: 0,
        pid: 0,
    }
}

impl Drop for Busy {
    fn drop(&mut self) {
        let mut done = whole_file(F_UNLCK);

        unsafe {
            fcntl(self.0.as_raw_fd(), F_SETLK, &mut done);
        }
    }
}

pub fn claim(path: &Path) -> Option<Busy> {
    lock(path, F_RDLCK)
}

pub fn reserve(path: &Path) -> Option<Busy> {
    lock(path, F_WRLCK)
}

fn lock(path: &Path, kind: c_short) -> Option<Busy> {
    let file = File::options()
        .create(true)
        .read(true)
        .write(true)
        .truncate(false)
        .open(path)
        .ok()?;
    let mut wanted = whole_file(kind);

    if unsafe { fcntl(file.as_raw_fd(), F_SETLK, &mut wanted) } == -1 {
        return None;
    }

    Some(Busy(file))
}

pub fn encode(argv: &[OsString]) -> Vec<u8> {
    let mut out = Vec::new();
    out.extend((argv.len() as u32).to_le_bytes());

    for argument in argv {
        push(&mut out, argument.as_bytes());
    }

    out
}

pub fn decode(bytes: &[u8]) -> Option<Vec<OsString>> {
    let mut cursor = 0;
    let count = take_u32(bytes, &mut cursor)?;
    let mut argv = Vec::with_capacity(count as usize);

    for _ in 0..count {
        argv.push(take_os(bytes, &mut cursor)?);
    }

    (cursor == bytes.len()).then_some(argv)
}

fn push(out: &mut Vec<u8>, bytes: &[u8]) {
    out.extend((bytes.len() as u32).to_le_bytes());
    out.extend(bytes);
}

fn take_u32(bytes: &[u8], cursor: &mut usize) -> Option<u32> {
    let end = cursor.checked_add(4)?;
    let value = u32::from_le_bytes(bytes.get(*cursor..end)?.try_into().ok()?);
    *cursor = end;
    Some(value)
}

fn take_os(bytes: &[u8], cursor: &mut usize) -> Option<OsString> {
    let length = take_u32(bytes, cursor)? as usize;
    let end = cursor.checked_add(length)?;
    let value = OsString::from_vec(bytes.get(*cursor..end)?.to_vec());
    *cursor = end;
    Some(value)
}

fn send_job(stream: &UnixStream, payload: &[u8], fds: [RawFd; 3]) -> io::Result<()> {
    let mut framed = Vec::with_capacity(payload.len() + 4);
    framed.extend((payload.len() as u32).to_le_bytes());
    framed.extend(payload);

    let mut control = [0u8; CONTROL_SPACE];
    let header = control.as_mut_ptr().cast::<CmsgHdr>();

    unsafe {
        (*header).len = CONTROL_LEN;
        (*header).level = SOL_SOCKET;
        (*header).kind = SCM_RIGHTS;
        let slots = control.as_mut_ptr().add(CMSG_HEADER).cast::<c_int>();
        for (slot, fd) in fds.iter().enumerate() {
            slots.add(slot).write(*fd);
        }
    }

    let iov = IoVec {
        base: framed.as_ptr().cast(),
        len: framed.len(),
    };
    let message = MsgHdr {
        name: ptr::null_mut(),
        namelen: 0,
        iov: &iov as *const IoVec as *mut IoVec,
        iovlen: 1,
        control: control.as_mut_ptr().cast(),
        controllen: CONTROL_SPACE,
        flags: 0,
    };

    let sent = unsafe { sendmsg(stream.as_raw_fd(), &message, 0) };

    if sent < 0 {
        return Err(io::Error::last_os_error());
    }
    if sent as usize != framed.len() {
        return Err(io::Error::other("the job was cut short"));
    }

    Ok(())
}

fn receive_job(stream: &mut UnixStream) -> io::Result<(Vec<u8>, Vec<RawFd>)> {
    let mut buffer = vec![0u8; JOB_LIMIT];
    let mut control = [0u8; CONTROL_SPACE];
    let iov = IoVec {
        base: buffer.as_mut_ptr().cast(),
        len: buffer.len(),
    };
    let mut message = MsgHdr {
        name: ptr::null_mut(),
        namelen: 0,
        iov: &iov as *const IoVec as *mut IoVec,
        iovlen: 1,
        control: control.as_mut_ptr().cast(),
        controllen: CONTROL_SPACE,
        flags: 0,
    };

    let read = unsafe { recvmsg(stream.as_raw_fd(), &mut message, MSG_CMSG_CLOEXEC) };

    if read <= 0 {
        return Err(io::Error::last_os_error());
    }

    let mut passed = Vec::new();
    let header = control.as_ptr().cast::<CmsgHdr>();

    if message.controllen >= CMSG_HEADER {
        unsafe {
            if (*header).level == SOL_SOCKET && (*header).kind == SCM_RIGHTS {
                let count = ((*header).len - CMSG_HEADER) / std::mem::size_of::<c_int>();
                let fds = control.as_ptr().add(CMSG_HEADER).cast::<c_int>();
                for slot in 0..count {
                    passed.push(fds.add(slot).read());
                }
            }
        }
    }

    let read = read as usize;

    if read < 4 {
        return Err(io::Error::other("the job carried no length"));
    }

    let length = u32::from_le_bytes(buffer[..4].try_into().unwrap_or_default()) as usize;
    let mut payload = buffer[4..read].to_vec();

    if payload.len() < length {
        let mut rest = vec![0u8; length - payload.len()];
        stream.read_exact(&mut rest)?;
        payload.extend(rest);
    }

    payload.truncate(length);

    Ok((payload, passed))
}

fn write_frame(stream: &mut UnixStream, payload: &[u8]) -> io::Result<()> {
    stream.write_all(&(payload.len() as u32).to_le_bytes())?;
    stream.write_all(payload)?;
    stream.flush()
}

fn read_frame(stream: &mut UnixStream) -> io::Result<Vec<u8>> {
    let mut header = [0u8; 4];
    stream.read_exact(&mut header)?;
    let mut payload = vec![0u8; u32::from_le_bytes(header) as usize];
    stream.read_exact(&mut payload)?;
    Ok(payload)
}

pub fn submit<S>(socket: &Path, lock: &Path, argv: &[OsString], start: S) -> io::Result<i32>
where
    S: Fn() -> io::Result<std::process::Child>,
{
    let spare = File::open("/dev/null").ok();
    let fds = stdio(spare.as_ref().map_or(-1, |file| file.as_raw_fd()));
    let mut last = io::Error::other("no attempt was made");

    for _ in 0..ATTEMPTS {
        let mut stream = match connect_or_start(socket, lock, &start) {
            Ok(stream) => stream,
            Err(error) => {
                last = error;
                continue;
            }
        };

        if let Err(error) = send_job(&stream, &encode(argv), fds) {
            last = error;
            continue;
        }

        match read_frame(&mut stream) {
            Ok(payload) if payload.len() == 4 => {
                return Ok(i32::from_le_bytes(
                    payload[..4].try_into().unwrap_or_default(),
                ))
            }
            Ok(_) => last = io::Error::other("the wine session sent a malformed status"),
            Err(error) => last = error,
        }
    }

    Err(last)
}

pub fn join(socket: &Path, argv: &[OsString]) -> io::Result<Option<i32>> {
    let mut stream = match UnixStream::connect(socket) {
        Ok(stream) => stream,
        Err(error)
            if matches!(
                error.kind(),
                io::ErrorKind::NotFound | io::ErrorKind::ConnectionRefused
            ) =>
        {
            return Ok(None)
        }
        Err(error) => return Err(error),
    };
    let spare = File::open("/dev/null").ok();
    let fds = stdio(spare.as_ref().map_or(-1, |file| file.as_raw_fd()));

    send_job(&stream, &encode(argv), fds)?;

    let status = match read_frame(&mut stream)? {
        payload if payload.len() == 4 => Some(i32::from_le_bytes(
            payload[..4].try_into().unwrap_or_default(),
        )),
        _ => return Err(io::Error::other("the wine session sent a malformed status")),
    };

    Ok(status)
}

fn stdio(fallback: RawFd) -> [RawFd; 3] {
    let mut fds = [fallback; 3];

    for (slot, fd) in fds.iter_mut().enumerate() {
        let given = slot as RawFd;
        if unsafe { fcntl(given, F_GETFD) } != -1 {
            *fd = given;
        }
    }

    fds
}

fn connect_or_start<S>(socket: &Path, lock: &Path, start: &S) -> io::Result<UnixStream>
where
    S: Fn() -> io::Result<std::process::Child>,
{
    if let Ok(stream) = UnixStream::connect(socket) {
        return Ok(stream);
    }

    let file = File::create(lock)?;
    let _guard = Lock::hold(file.as_raw_fd())?;

    if let Ok(stream) = UnixStream::connect(socket) {
        return Ok(stream);
    }

    let _ = std::fs::remove_file(socket);
    let mut starting = start()?;

    let deadline = Instant::now() + START_TIMEOUT;

    loop {
        if let Ok(stream) = UnixStream::connect(socket) {
            return Ok(stream);
        }
        if let Ok(Some(status)) = starting.try_wait() {
            return Err(io::Error::other(format!(
                "the wine session gave up with {}",
                exit_code(status)
            )));
        }
        if Instant::now() >= deadline {
            return Err(io::Error::other("the wine session did not start"));
        }
        thread::sleep(TICK);
    }
}

struct Lock(RawFd);

impl Lock {
    fn hold(fd: RawFd) -> io::Result<Self> {
        if unsafe { flock(fd, LOCK_EX) } == -1 {
            return Err(io::Error::last_os_error());
        }

        Ok(Self(fd))
    }
}

impl Drop for Lock {
    fn drop(&mut self) {
        unsafe {
            flock(self.0, LOCK_UN);
        }
    }
}

pub fn run_broker(args: &[OsString]) -> i32 {
    let Some((socket, rest)) = args.split_first() else {
        eprintln!("cabinet-wine: a wine session needs a socket");
        return 127;
    };
    let Some(runner) = rest.first() else {
        eprintln!("cabinet-wine: a wine session needs a Wine runner");
        return 127;
    };
    let socket = PathBuf::from(socket);
    let lock = socket.with_extension("lock");

    if unsafe { prctl(PR_SET_CHILD_SUBREAPER, 1) } == -1 {
        eprintln!(
            "cabinet-wine: cannot become a child subreaper: {}",
            io::Error::last_os_error()
        );
        return 127;
    }

    let listener = match UnixListener::bind(&socket) {
        Ok(listener) => listener,
        Err(error) => {
            eprintln!("cabinet-wine: cannot serve {socket:?}: {error}");
            return 127;
        }
    };

    if listener.set_nonblocking(true).is_err() {
        eprintln!("cabinet-wine: cannot poll {socket:?}");
        return 127;
    }

    let watcher = x11::Watcher::start();
    let live = Arc::new(AtomicUsize::new(0));
    let owned = Arc::new(Mutex::new(Vec::new()));
    let mut idle = Some(Instant::now());

    loop {
        match listener.accept() {
            Ok((stream, _)) => {
                idle = None;
                live.fetch_add(1, Ordering::SeqCst);
                let runner = runner.clone();
                let counted = Job(Arc::clone(&live));
                let owned = Arc::clone(&owned);
                thread::spawn(move || {
                    let _counted = counted;
                    serve(stream, &runner, &owned);
                });
            }
            Err(error) if error.kind() == io::ErrorKind::WouldBlock => {
                reap_orphans(&owned);

                if live.load(Ordering::SeqCst) > 0 {
                    idle = None;
                } else {
                    match idle {
                        None => idle = Some(Instant::now()),
                        Some(since) if since.elapsed() >= IDLE_GRACE => {
                            if retire(&lock, &socket, &live) {
                                break;
                            }
                            idle = None;
                        }
                        Some(_) => {}
                    }
                }

                thread::sleep(TICK);
            }
            Err(error) => {
                eprintln!("cabinet-wine: cannot accept on {socket:?}: {error}");
                break;
            }
        }
    }

    drop(watcher);
    reap_orphans(&owned);

    0
}

struct Job(Arc<AtomicUsize>);

impl Drop for Job {
    fn drop(&mut self) {
        self.0.fetch_sub(1, Ordering::SeqCst);
    }
}

fn retire(lock: &Path, socket: &Path, live: &AtomicUsize) -> bool {
    let Ok(file) = File::create(lock) else {
        return false;
    };
    let Ok(_guard) = Lock::hold(file.as_raw_fd()) else {
        return false;
    };

    if live.load(Ordering::SeqCst) > 0 {
        return false;
    }

    let _ = std::fs::remove_file(socket);

    true
}

fn serve(mut stream: UnixStream, runner: &OsStr, owned: &Mutex<Vec<i32>>) {
    let Ok((payload, passed)) = receive_job(&mut stream) else {
        return;
    };
    let Some(argv) = decode(&payload) else {
        let _ = write_frame(&mut stream, &127i32.to_le_bytes());
        return;
    };

    let status = match spawn(runner, &argv, passed) {
        Ok(mut child) => {
            let group = child.id() as i32;
            remember(owned, group);
            let status = supervise(&mut child, group, argv.first(), &mut stream);
            forget(owned, group);
            status
        }
        Err(error) => {
            eprintln!("cabinet-wine: cannot start Wine {runner:?}: {error}");
            127
        }
    };

    let _ = write_frame(&mut stream, &status.to_le_bytes());
}

fn spawn(runner: &OsStr, argv: &[OsString], passed: Vec<RawFd>) -> io::Result<std::process::Child> {
    let mut command = Command::new(runner);
    command.args(argv);
    command.env_remove("WINELOADER");

    if let [input, output, error] = passed[..] {
        unsafe {
            command.stdin(Stdio::from_raw_fd(input));
            command.stdout(Stdio::from_raw_fd(output));
            command.stderr(Stdio::from_raw_fd(error));
        }
    }

    let session = unsafe { getpid() };

    unsafe {
        command.pre_exec(move || {
            if prctl(PR_SET_PDEATHSIG, SIGKILL) == -1 {
                return Err(io::Error::last_os_error());
            }
            if getppid() != session {
                return Err(io::Error::other(
                    "the wine session went away while starting",
                ));
            }
            if setpgid(0, 0) == -1 {
                return Err(io::Error::last_os_error());
            }

            Ok(())
        });
    }

    command.spawn()
}

fn supervise(
    child: &mut std::process::Child,
    group: i32,
    host: Option<&OsString>,
    stream: &mut UnixStream,
) -> i32 {
    let expected = host.and_then(|host| expected_process_name(host));
    let mut seen: Option<(ProcessInfo, Option<PidFd>)> = None;

    loop {
        match child.try_wait() {
            Ok(Some(status)) => {
                terminate_tree(group);
                return exit_code(status);
            }
            Ok(None) => {}
            Err(error) => {
                eprintln!("cabinet-wine: cannot check Wine: {error}");
                terminate_tree(group);
                let _ = child.wait();
                return 127;
            }
        }

        if hung_up(stream) {
            terminate_tree(group);
            let _ = child.wait();
            return 128 + SIGTERM;
        }

        if let Some(expected) = expected.as_deref() {
            let processes = process_snapshot();

            if let Some(observed) = seen.as_ref() {
                if !alive(observed, &processes) {
                    terminate_tree(group);
                    let status = child.wait();
                    return status.map(exit_code).unwrap_or(127);
                }
            } else if let Some(found) = find_host(&processes, group, expected) {
                if found.state == b'Z' {
                    terminate_tree(group);
                    let status = child.wait();
                    return status.map(exit_code).unwrap_or(127);
                }

                seen = Some((found.clone(), PidFd::open(&found)));
            }
        }

        thread::sleep(TICK);
    }
}

fn alive(observed: &(ProcessInfo, Option<PidFd>), processes: &[ProcessInfo]) -> bool {
    match observed.1.as_ref() {
        Some(pidfd) => !pidfd.signalled(),
        None => processes
            .iter()
            .find(|process| process.pid == observed.0.pid)
            .is_some_and(|process| {
                process.start_time == observed.0.start_time
                    && process.state != b'Z'
                    && process.comm == observed.0.comm
            }),
    }
}

fn hung_up(stream: &UnixStream) -> bool {
    let mut pollfd = PollFd {
        fd: stream.as_raw_fd(),
        events: POLLIN,
        revents: 0,
    };
    let result = unsafe { poll(&mut pollfd, 1, 0) };

    result > 0 && pollfd.revents & (POLLIN | POLLERR | POLLHUP) != 0
}

fn remember(owned: &Mutex<Vec<i32>>, pid: i32) {
    if let Ok(mut owned) = owned.lock() {
        owned.push(pid);
    }
}

fn forget(owned: &Mutex<Vec<i32>>, pid: i32) {
    if let Ok(mut owned) = owned.lock() {
        owned.retain(|held| *held != pid);
    }
}

fn reap_orphans(owned: &Mutex<Vec<i32>>) {
    let Ok(owned) = owned.lock() else {
        return;
    };
    let mine = unsafe { getpid() };

    for process in process_snapshot() {
        if process.parent == mine && process.state == b'Z' && !owned.contains(&process.pid) {
            let mut status = 0;
            unsafe {
                waitpid(process.pid, &mut status, WNOHANG);
            }
        }
    }
}

pub fn exit_code(status: std::process::ExitStatus) -> i32 {
    status
        .code()
        .unwrap_or_else(|| 128 + status.signal().unwrap_or(1))
}

pub fn expected_process_name(host: &OsStr) -> Option<String> {
    let mut name = Path::new(host).file_name()?.to_string_lossy().into_owned();

    if name.ends_with(".so") {
        name.truncate(name.len() - 3);
    }
    if !name.to_ascii_lowercase().ends_with(".exe") {
        name.push_str(".exe");
    }

    name.truncate(name.len().min(15));

    Some(name)
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ProcessInfo {
    pub pid: i32,
    pub parent: i32,
    pub group: i32,
    pub state: u8,
    pub start_time: u64,
    pub comm: String,
}

pub fn parse_process_stat(stat: &str) -> Option<ProcessInfo> {
    let open = stat.find('(')?;
    let close = stat.rfind(')')?;
    let pid = stat[..open].trim().parse().ok()?;
    let fields: Vec<&str> = stat[close + 1..].split_whitespace().collect();
    let state = fields.first()?.as_bytes().first().copied()?;
    let parent = fields.get(1)?.parse().ok()?;
    let group = fields.get(2)?.parse().ok()?;
    let start_time = fields.get(19)?.parse().ok()?;

    Some(ProcessInfo {
        pid,
        parent,
        group,
        state,
        start_time,
        comm: stat[open + 1..close].to_string(),
    })
}

fn process_snapshot() -> Vec<ProcessInfo> {
    let Ok(entries) = std::fs::read_dir("/proc") else {
        return Vec::new();
    };

    entries
        .filter_map(Result::ok)
        .filter_map(|entry| entry.file_name().to_str()?.parse::<i32>().ok())
        .filter_map(process_info)
        .collect()
}

fn process_info(pid: i32) -> Option<ProcessInfo> {
    parse_process_stat(&std::fs::read_to_string(format!("/proc/{pid}/stat")).ok()?)
}

pub fn find_host(processes: &[ProcessInfo], group: i32, expected: &str) -> Option<ProcessInfo> {
    processes
        .iter()
        .find(|process| process.group == group && process.comm == expected)
        .cloned()
}

fn terminate_tree(group: i32) {
    unsafe {
        kill(-group, SIGTERM);
    }

    for _ in 0..10 {
        if !has_live(group) {
            return;
        }
        thread::sleep(TICK);
    }

    unsafe {
        kill(-group, SIGKILL);
    }

    for _ in 0..20 {
        if !has_live(group) {
            return;
        }
        thread::sleep(TICK);
    }
}

fn has_live(group: i32) -> bool {
    process_snapshot()
        .iter()
        .any(|process| process.state != b'Z' && process.group == group)
}

struct PidFd(RawFd);

impl PidFd {
    fn open(expected: &ProcessInfo) -> Option<Self> {
        let fd = unsafe { syscall(SYS_PIDFD_OPEN, expected.pid as c_long, 0) };
        let pidfd = (fd >= 0).then_some(Self(fd as RawFd))?;
        let current = process_info(expected.pid)?;

        (current.start_time == expected.start_time && current.comm == expected.comm)
            .then_some(pidfd)
    }

    fn signalled(&self) -> bool {
        let mut pollfd = PollFd {
            fd: self.0,
            events: POLLIN,
            revents: 0,
        };
        let result = unsafe { poll(&mut pollfd, 1, 0) };

        result > 0 && pollfd.revents & (POLLIN | POLLERR | POLLHUP) != 0
    }
}

impl Drop for PidFd {
    fn drop(&mut self) {
        unsafe {
            close(self.0);
        }
    }
}

const PR_SET_PDEATHSIG: i32 = 1;
const PR_SET_CHILD_SUBREAPER: i32 = 36;
const SIGKILL: i32 = 9;
const SIGTERM: i32 = 15;
const WNOHANG: i32 = 1;
const POLLIN: i16 = 1;
const POLLERR: i16 = 8;
const POLLHUP: i16 = 16;
const SYS_PIDFD_OPEN: c_long = 434;
const LOCK_EX: c_int = 2;
const F_SETLK: c_int = 6;
const F_RDLCK: c_short = 0;
const F_WRLCK: c_short = 1;
const F_UNLCK: c_short = 2;
const SEEK_SET: c_short = 0;

#[repr(C)]
struct FileLock {
    kind: c_short,
    whence: c_short,
    start: i64,
    len: i64,
    pid: i32,
}
const LOCK_UN: c_int = 8;
const SOL_SOCKET: c_int = 1;
const SCM_RIGHTS: c_int = 1;
const MSG_CMSG_CLOEXEC: c_int = 0x4000_0000;
const F_GETFD: c_int = 1;
const CMSG_HEADER: usize = 16;
const CONTROL_LEN: usize = CMSG_HEADER + 3 * 4;
const CONTROL_SPACE: usize = CMSG_HEADER + 16;
const JOB_LIMIT: usize = 64 * 1024;

#[repr(C)]
struct IoVec {
    base: *const u8,
    len: usize,
}

#[repr(C)]
struct MsgHdr {
    name: *mut c_void,
    namelen: u32,
    iov: *mut IoVec,
    iovlen: usize,
    control: *mut c_void,
    controllen: usize,
    flags: c_int,
}

#[repr(C)]
struct CmsgHdr {
    len: usize,
    level: c_int,
    kind: c_int,
}

#[repr(C)]
struct PollFd {
    fd: c_int,
    events: i16,
    revents: i16,
}

extern "C" {
    fn close(fd: c_int) -> c_int;
    fn flock(fd: c_int, operation: c_int) -> c_int;
    fn getpid() -> i32;
    fn getppid() -> i32;
    fn kill(pid: i32, signal: i32) -> i32;
    fn poll(fds: *mut PollFd, count: usize, timeout: c_int) -> c_int;
    fn fcntl(fd: c_int, command: c_int, ...) -> c_int;
    fn prctl(option: i32, ...) -> i32;
    fn recvmsg(fd: c_int, message: *mut MsgHdr, flags: c_int) -> isize;
    fn sendmsg(fd: c_int, message: *const MsgHdr, flags: c_int) -> isize;
    fn setpgid(pid: i32, pgid: i32) -> i32;
    fn syscall(number: c_long, ...) -> c_long;
    fn waitpid(pid: i32, status: *mut i32, options: i32) -> i32;
}

#[cfg(test)]
mod tests {
    use super::*;

    fn process(pid: i32, parent: i32, state: u8, comm: &str, start_time: u64) -> ProcessInfo {
        ProcessInfo {
            pid,
            parent,
            group: parent,
            state,
            start_time,
            comm: comm.to_string(),
        }
    }

    fn grouped(pid: i32, parent: i32, group: i32, state: u8, comm: &str) -> ProcessInfo {
        ProcessInfo {
            pid,
            parent,
            group,
            state,
            start_time: pid as u64,
            comm: comm.to_string(),
        }
    }

    #[test]
    fn host_process_names_match_wines_truncated_executable_name() {
        assert_eq!(
            expected_process_name(OsStr::new("/app/yabridge-host.exe.so")),
            Some("yabridge-host.e".to_string())
        );
        assert_eq!(
            expected_process_name(OsStr::new("/app/yabridge-host-32.exe.so")),
            Some("yabridge-host-3".to_string())
        );
        assert_eq!(
            expected_process_name(OsStr::new("cmd")),
            Some("cmd.exe".to_string())
        );
    }

    #[test]
    fn process_stat_parsing_handles_parentheses_in_the_command_name() {
        let mut fields = vec!["S".to_string(), "100".to_string(), "100".to_string()];
        fields.extend((0..16).map(|_| "0".to_string()));
        fields.push("42".to_string());
        let stat = format!("123 (name ) with paren) {}", fields.join(" "));

        assert_eq!(
            parse_process_stat(&stat),
            Some(process(123, 100, b'S', "name ) with paren", 42))
        );
    }

    #[test]
    fn a_job_owns_the_host_in_its_own_process_group() {
        let processes = vec![
            grouped(11, 9, 11, b'S', "start.exe"),
            grouped(12, 9, 11, b'S', "yabridge-host.e"),
            grouped(13, 9, 20, b'S', "yabridge-host.e"),
            grouped(14, 9, 11, b'S', "wineserver"),
        ];

        assert_eq!(
            find_host(&processes, 11, "yabridge-host.e").unwrap().pid,
            12
        );
        assert_eq!(
            find_host(&processes, 20, "yabridge-host.e").unwrap().pid,
            13
        );
        assert_eq!(find_host(&processes, 30, "yabridge-host.e"), None);
    }

    #[test]
    fn a_job_carries_the_calling_plugins_own_stdio() {
        let (client, mut server) = UnixStream::pair().unwrap();
        let argv: Vec<OsString> = ["host.exe.so", "vst3"].iter().map(OsString::from).collect();
        let file = std::fs::File::open("/dev/null").unwrap();
        let spare = file.as_raw_fd();

        send_job(&client, &encode(&argv), [spare, spare, spare]).unwrap();

        let (payload, passed) = receive_job(&mut server).unwrap();

        assert_eq!(decode(&payload), Some(argv));
        assert_eq!(passed.len(), 3);
        assert!(passed.iter().all(|fd| *fd >= 0 && *fd != spare));

        for fd in passed {
            unsafe {
                close(fd);
            }
        }

        drop(file);
    }

    #[test]
    fn a_session_of_one_prefix_never_collides_with_another() {
        let mut seen = Vec::new();

        for prefix in [
            "/p/klevgrand",
            "/p/helix-native",
            "/p/fabfilter",
            "/p/serum",
        ] {
            let name = key(OsStr::new(prefix));
            assert!(!seen.contains(&name), "{prefix} collided");
            seen.push(name);
        }
    }
}
