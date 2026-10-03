use std::ffi::OsString;
use std::io::{self, Read, Write};
use std::os::unix::io::{AsRawFd, RawFd};
use std::os::unix::net::UnixStream;
use std::path::{Path, PathBuf};
use std::time::{Duration, Instant};

const DRIVER: &str = "org.freedesktop.DBus";
const DRIVER_PATH: &str = "/org/freedesktop/DBus";
const METHOD_CALL: u8 = 1;
const METHOD_RETURN: u8 = 2;
const ERROR: u8 = 3;
const PATH: u8 = 1;
const INTERFACE: u8 = 2;
const MEMBER: u8 = 3;
const ERROR_NAME: u8 = 4;
const REPLY_SERIAL: u8 = 5;
const DESTINATION: u8 = 6;
const SIGNATURE: u8 = 8;
const DO_NOT_QUEUE: u32 = 4;
const PRIMARY_OWNER: u32 = 1;
const MESSAGE_LIMIT: usize = 1 << 20;
const REPLY_TIMEOUT: Duration = Duration::from_secs(30);

pub struct Bus {
    stream: UnixStream,
    serial: u32,
    timeout: Duration,
}

pub struct Message {
    kind: u8,
    reply_serial: Option<u32>,
    error: Option<String>,
    body: Vec<u8>,
}

impl Bus {
    pub fn session<E>(getenv: E) -> io::Result<Self>
    where
        E: Fn(&str) -> Option<OsString>,
    {
        let path = address(&getenv).ok_or_else(|| io::Error::other("no session bus address"))?;
        Self::connect(&path, REPLY_TIMEOUT)
    }

    fn connect(path: &Path, timeout: Duration) -> io::Result<Self> {
        let mut stream = UnixStream::connect(path)?;
        stream.set_read_timeout(Some(timeout))?;
        stream.set_write_timeout(Some(timeout))?;

        stream.write_all(&authentication(unsafe { getuid() }))?;
        let answer = line(&mut stream)?;
        if !answer.starts_with(b"OK ") {
            return Err(io::Error::other("the session bus refused authentication"));
        }
        stream.write_all(b"BEGIN\r\n")?;

        let mut bus = Self {
            stream,
            serial: 0,
            timeout,
        };
        bus.call("Hello", &[])?;
        Ok(bus)
    }

    pub fn start_service(&mut self, name: &str) -> io::Result<()> {
        self.call(
            "StartServiceByName",
            &[Argument::Text(name), Argument::Number(0)],
        )
        .map(drop)
    }

    pub fn own(&mut self, name: &str) -> io::Result<()> {
        let reply = self.call(
            "RequestName",
            &[Argument::Text(name), Argument::Number(DO_NOT_QUEUE)],
        )?;

        match number(&reply.body) {
            Some(PRIMARY_OWNER) => Ok(()),
            _ => Err(io::Error::other(format!("{name} already has an owner"))),
        }
    }

    pub fn fd(&self) -> RawFd {
        self.stream.as_raw_fd()
    }

    pub fn discard(&mut self) -> io::Result<()> {
        receive(&mut self.stream).map(drop)
    }

    fn call(&mut self, member: &str, arguments: &[Argument]) -> io::Result<Message> {
        self.serial += 1;
        self.stream
            .write_all(&method_call(self.serial, member, arguments))?;
        let deadline = Instant::now() + self.timeout;

        loop {
            let left = deadline
                .checked_duration_since(Instant::now())
                .filter(|left| !left.is_zero())
                .ok_or_else(|| {
                    io::Error::new(io::ErrorKind::TimedOut, "the session bus never answered")
                })?;
            self.stream.set_read_timeout(Some(left))?;
            let message = receive(&mut self.stream)?;
            if message.reply_serial != Some(self.serial) {
                continue;
            }

            return match message.kind {
                METHOD_RETURN => Ok(message),
                ERROR => Err(io::Error::other(
                    message.error.unwrap_or_else(|| member.to_string()),
                )),
                _ => continue,
            };
        }
    }
}

fn address<E>(getenv: &E) -> Option<PathBuf>
where
    E: Fn(&str) -> Option<OsString>,
{
    match getenv("DBUS_SESSION_BUS_ADDRESS") {
        Some(address) => address
            .to_str()?
            .split(';')
            .find_map(|entry| {
                entry
                    .strip_prefix("unix:")?
                    .split(',')
                    .find_map(|pair| pair.strip_prefix("path="))
            })
            .map(PathBuf::from),
        None => getenv("XDG_RUNTIME_DIR").map(|runtime| PathBuf::from(runtime).join("bus")),
    }
}

fn authentication(uid: u32) -> Vec<u8> {
    let hex: String = uid
        .to_string()
        .bytes()
        .map(|byte| format!("{byte:02x}"))
        .collect();

    format!("\0AUTH EXTERNAL {hex}\r\n").into_bytes()
}

fn line(stream: &mut UnixStream) -> io::Result<Vec<u8>> {
    let mut read = Vec::new();
    let mut byte = [0u8];

    while !read.ends_with(b"\r\n") {
        stream.read_exact(&mut byte)?;
        read.push(byte[0]);
        if read.len() > 512 {
            return Err(io::Error::other("the session bus sent an overlong line"));
        }
    }

    Ok(read)
}

enum Argument<'a> {
    Text(&'a str),
    Number(u32),
}

struct Writer(Vec<u8>);

impl Writer {
    fn align(&mut self, to: usize) {
        while self.0.len() % to != 0 {
            self.0.push(0);
        }
    }

    fn byte(&mut self, value: u8) {
        self.0.push(value);
    }

    fn number(&mut self, value: u32) {
        self.align(4);
        self.0.extend(value.to_le_bytes());
    }

    fn text(&mut self, value: &str) {
        self.number(value.len() as u32);
        self.0.extend(value.as_bytes());
        self.0.push(0);
    }

    fn signature(&mut self, value: &str) {
        self.0.push(value.len() as u8);
        self.0.extend(value.as_bytes());
        self.0.push(0);
    }

    fn field(&mut self, code: u8, kind: &str, write: impl FnOnce(&mut Self)) {
        self.align(8);
        self.byte(code);
        self.signature(kind);
        write(self);
    }
}

fn method_call(serial: u32, member: &str, arguments: &[Argument]) -> Vec<u8> {
    let mut body = Writer(Vec::new());
    let mut signature = String::new();

    for argument in arguments {
        match argument {
            Argument::Text(value) => {
                body.text(value);
                signature.push('s');
            }
            Argument::Number(value) => {
                body.number(*value);
                signature.push('u');
            }
        }
    }

    let mut fields = Writer(Vec::new());
    fields.field(PATH, "o", |out| out.text(DRIVER_PATH));
    fields.field(INTERFACE, "s", |out| out.text(DRIVER));
    fields.field(MEMBER, "s", |out| out.text(member));
    fields.field(DESTINATION, "s", |out| out.text(DRIVER));
    if !signature.is_empty() {
        fields.field(SIGNATURE, "g", |out| out.signature(&signature));
    }

    let mut message = Writer(vec![b'l', METHOD_CALL, 0, 1]);
    message.number(body.0.len() as u32);
    message.number(serial);
    message.number(fields.0.len() as u32);
    message.align(8);
    message.0.extend(fields.0);
    message.align(8);
    message.0.extend(body.0);
    message.0
}

fn receive(stream: &mut UnixStream) -> io::Result<Message> {
    let mut fixed = [0u8; 16];
    stream.read_exact(&mut fixed)?;

    if fixed[0] != b'l' {
        return Err(io::Error::other("the session bus speaks big-endian"));
    }

    let body = u32::from_le_bytes(fixed[4..8].try_into().unwrap_or_default()) as usize;
    let fields = u32::from_le_bytes(fixed[12..16].try_into().unwrap_or_default()) as usize;
    let header = (16 + fields).next_multiple_of(8);

    if header + body > MESSAGE_LIMIT {
        return Err(io::Error::other(
            "the session bus sent an oversized message",
        ));
    }

    let mut rest = vec![0u8; header - 16 + body];
    stream.read_exact(&mut rest)?;

    let mut whole = fixed.to_vec();
    whole.extend(rest);

    parse(&whole, fields, header).ok_or_else(|| io::Error::other("a malformed bus message"))
}

fn parse(whole: &[u8], fields: usize, header: usize) -> Option<Message> {
    let mut message = Message {
        kind: whole[1],
        reply_serial: None,
        error: None,
        body: whole.get(header..)?.to_vec(),
    };
    let end = 16 + fields;
    let mut cursor = 16;

    while cursor < end {
        cursor = cursor.next_multiple_of(8);
        let code = *whole.get(cursor)?;
        let length = *whole.get(cursor + 1)? as usize;
        let kind = whole.get(cursor + 2..cursor + 2 + length)?.to_vec();
        cursor += 3 + length;

        match kind.as_slice() {
            b"u" => {
                cursor = cursor.next_multiple_of(4);
                let value = u32::from_le_bytes(whole.get(cursor..cursor + 4)?.try_into().ok()?);
                cursor += 4;
                if code == REPLY_SERIAL {
                    message.reply_serial = Some(value);
                }
            }
            b"s" | b"o" => {
                cursor = cursor.next_multiple_of(4);
                let length =
                    u32::from_le_bytes(whole.get(cursor..cursor + 4)?.try_into().ok()?) as usize;
                let value = whole.get(cursor + 4..cursor + 4 + length)?;
                cursor += 5 + length;
                if code == ERROR_NAME {
                    message.error = Some(String::from_utf8_lossy(value).into_owned());
                }
            }
            b"g" => {
                let length = *whole.get(cursor)? as usize;
                cursor += 2 + length;
            }
            _ => return None,
        }
    }

    Some(message)
}

fn number(body: &[u8]) -> Option<u32> {
    Some(u32::from_le_bytes(body.get(..4)?.try_into().ok()?))
}

extern "C" {
    fn getuid() -> u32;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_method_call_is_marshalled_as_the_specification_lays_it_out() {
        let call = method_call(
            2,
            "StartServiceByName",
            &[Argument::Text("a.b.C"), Argument::Number(0)],
        );

        assert_eq!(&call[..4], b"l\x01\x00\x01");
        assert_eq!(&call[4..8], &16u32.to_le_bytes());
        assert_eq!(&call[8..12], &2u32.to_le_bytes());
        assert_eq!(call.len() % 8, 0);
        assert_eq!(
            &call[call.len() - 16..],
            b"\x05\x00\x00\x00a.b.C\x00\x00\x00\x00\x00\x00\x00"
        );
        assert!(call
            .windows(b"\x08\x01g\x00\x02su\x00".len())
            .any(|window| window == b"\x08\x01g\x00\x02su\x00"));
    }

    #[test]
    fn a_call_without_arguments_carries_no_signature() {
        let call = method_call(1, "Hello", &[]);

        assert_eq!(&call[4..8], &0u32.to_le_bytes());
        assert!(!call.windows(3).any(|window| window == b"\x08\x01g"));
    }

    #[test]
    fn a_parsed_error_names_its_error_and_the_call_it_answers() {
        let mut fields = Writer(Vec::new());
        fields.field(ERROR_NAME, "s", |out| {
            out.text("org.freedesktop.DBus.Error.ServiceUnknown")
        });
        fields.field(REPLY_SERIAL, "u", |out| out.number(7));
        let mut whole = Writer(vec![b'l', ERROR, 0, 1]);
        whole.number(0);
        whole.number(9);
        whole.number(fields.0.len() as u32);
        whole.align(8);
        let length = fields.0.len();
        whole.0.extend(fields.0);
        whole.align(8);
        let header = whole.0.len();

        let message = parse(&whole.0, length, header).unwrap();

        assert_eq!(message.kind, ERROR);
        assert_eq!(message.reply_serial, Some(7));
        assert_eq!(
            message.error.as_deref(),
            Some("org.freedesktop.DBus.Error.ServiceUnknown")
        );
    }

    #[test]
    fn a_bus_that_never_answers_is_given_up_on() {
        let directory =
            std::env::temp_dir().join(format!("cabinet-silent-bus-{}", std::process::id()));
        std::fs::create_dir_all(&directory).unwrap();
        let path = directory.join("bus");
        let listener = std::os::unix::net::UnixListener::bind(&path).unwrap();
        let (release, released) = std::sync::mpsc::channel::<()>();
        let silent = std::thread::spawn(move || {
            let held = listener.accept().unwrap();
            let _ = released.recv();
            drop(held);
        });
        let started = std::time::Instant::now();

        let connected = Bus::connect(&path, Duration::from_millis(200));

        assert!(connected.is_err());
        assert!(started.elapsed() < Duration::from_secs(5));
        release.send(()).unwrap();
        silent.join().unwrap();
        std::fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn authentication_sends_the_uid_as_hex_encoded_decimal() {
        assert_eq!(
            authentication(1000),
            b"\0AUTH EXTERNAL 31303030\r\n".to_vec()
        );
    }

    #[test]
    fn the_bus_address_comes_from_its_unix_path() {
        let given = |key: &str| {
            (key == "DBUS_SESSION_BUS_ADDRESS")
                .then(|| OsString::from("tcp:host=x;unix:path=/run/user/1/bus,guid=0"))
        };
        let unset = |key: &str| (key == "XDG_RUNTIME_DIR").then(|| OsString::from("/run/user/1"));

        assert_eq!(address(&given), Some(PathBuf::from("/run/user/1/bus")));
        assert_eq!(address(&unset), Some(PathBuf::from("/run/user/1/bus")));
    }
}
