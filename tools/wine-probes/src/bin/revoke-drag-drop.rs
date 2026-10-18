use std::ffi::{c_void, CString};
use std::process::exit;
use std::sync::atomic::{AtomicUsize, Ordering};

type Hwnd = *mut c_void;
type Handle = *mut c_void;

const FAULTED_ON_THE_FOREIGN_TARGET: u32 = 0xdd;
const FAULTED_ELSEWHERE: u32 = 0xde;
const EXCEPTION_ACCESS_VIOLATION: u32 = 0xc000_0005;
const MEM_COMMIT: u32 = 0x1000;
const MEM_RESERVE: u32 = 0x2000;
const PAGE_READWRITE: u32 = 0x04;
const HWND_MESSAGE: isize = -3;
const QS_ALLINPUT: u32 = 0x04ff;
const PM_REMOVE: u32 = 0x0001;
const WAIT_OBJECT_0: u32 = 0;
const WAIT_TIMEOUT: u32 = 0x102;
const S_OK: i32 = 0;
const E_NOINTERFACE: i32 = 0x8000_4002_u32 as i32;

static FOREIGN_TARGET: AtomicUsize = AtomicUsize::new(0);

#[repr(C)]
#[derive(PartialEq, Eq)]
struct Guid {
    data1: u32,
    data2: u16,
    data3: u16,
    data4: [u8; 8],
}

const IID_IUNKNOWN: Guid = Guid {
    data1: 0,
    data2: 0,
    data3: 0,
    data4: [0xc0, 0, 0, 0, 0, 0, 0, 0x46],
};
const IID_IDROPTARGET: Guid = Guid {
    data1: 0x122,
    data2: 0,
    data3: 0,
    data4: [0xc0, 0, 0, 0, 0, 0, 0, 0x46],
};

#[repr(C)]
struct PointL {
    x: i32,
    y: i32,
}

#[repr(C)]
struct DropTarget {
    methods: *const Methods,
}

#[repr(C)]
struct Methods {
    query: unsafe extern "system" fn(*mut DropTarget, *const Guid, *mut *mut c_void) -> i32,
    add_ref: unsafe extern "system" fn(*mut DropTarget) -> u32,
    release: unsafe extern "system" fn(*mut DropTarget) -> u32,
    enter: unsafe extern "system" fn(*mut DropTarget, *mut c_void, u32, PointL, *mut u32) -> i32,
    over: unsafe extern "system" fn(*mut DropTarget, u32, PointL, *mut u32) -> i32,
    leave: unsafe extern "system" fn(*mut DropTarget) -> i32,
    drop: unsafe extern "system" fn(*mut DropTarget, *mut c_void, u32, PointL, *mut u32) -> i32,
}

static METHODS: Methods = Methods {
    query,
    add_ref: reference,
    release: reference,
    enter,
    over,
    leave,
    drop: enter,
};

#[repr(C)]
struct ExceptionRecord {
    code: u32,
    flags: u32,
    record: *mut ExceptionRecord,
    address: *mut c_void,
    parameters: u32,
    information: [usize; 15],
}

#[repr(C)]
struct ExceptionPointers {
    record: *const ExceptionRecord,
    context: *mut c_void,
}

#[repr(C, align(8))]
struct StartupInfo {
    size: u32,
    rest: [u8; 100],
}

#[repr(C)]
struct ProcessInformation {
    process: Handle,
    thread: Handle,
    process_id: u32,
    thread_id: u32,
}

#[repr(C, align(8))]
struct Message([u8; 64]);

#[link(name = "kernel32")]
extern "system" {
    fn SetUnhandledExceptionFilter(
        filter: unsafe extern "system" fn(*const ExceptionPointers) -> i32,
    ) -> *mut c_void;
    fn ExitProcess(code: u32) -> !;
    fn VirtualAlloc(address: *mut c_void, size: usize, kind: u32, protect: u32) -> *mut c_void;
    fn CreateProcessA(
        application: *const u8,
        command: *mut u8,
        process_attributes: *mut c_void,
        thread_attributes: *mut c_void,
        inherit: i32,
        flags: u32,
        environment: *mut c_void,
        directory: *const u8,
        startup: *mut StartupInfo,
        information: *mut ProcessInformation,
    ) -> i32;
    fn GetLastError() -> u32;
    fn GetTickCount64() -> u64;
    fn TerminateProcess(process: Handle, code: u32) -> i32;
    fn GetExitCodeProcess(process: Handle, code: *mut u32) -> i32;
}

#[link(name = "user32")]
extern "system" {
    fn CreateWindowExA(
        extended: u32,
        class: *const u8,
        name: *const u8,
        style: u32,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
        parent: Hwnd,
        menu: *mut c_void,
        instance: *mut c_void,
        parameter: *mut c_void,
    ) -> Hwnd;
    fn MsgWaitForMultipleObjects(
        count: u32,
        handles: *const Handle,
        all: i32,
        milliseconds: u32,
        wake: u32,
    ) -> u32;
    fn PeekMessageA(message: *mut Message, window: Hwnd, first: u32, last: u32, remove: u32)
        -> i32;
    fn DispatchMessageA(message: *const Message) -> isize;
}

#[link(name = "ole32")]
extern "system" {
    fn OleInitialize(reserved: *mut c_void) -> i32;
    fn RegisterDragDrop(window: Hwnd, target: *mut DropTarget) -> i32;
    fn RevokeDragDrop(window: Hwnd) -> i32;
}

unsafe extern "system" fn query(
    target: *mut DropTarget,
    iid: *const Guid,
    out: *mut *mut c_void,
) -> i32 {
    if *iid == IID_IUNKNOWN || *iid == IID_IDROPTARGET {
        *out = target.cast();
        return S_OK;
    }

    *out = std::ptr::null_mut();
    E_NOINTERFACE
}

unsafe extern "system" fn reference(_: *mut DropTarget) -> u32 {
    1
}

unsafe extern "system" fn enter(
    _: *mut DropTarget,
    _: *mut c_void,
    _: u32,
    _: PointL,
    _: *mut u32,
) -> i32 {
    S_OK
}

unsafe extern "system" fn over(_: *mut DropTarget, _: u32, _: PointL, _: *mut u32) -> i32 {
    S_OK
}

unsafe extern "system" fn leave(_: *mut DropTarget) -> i32 {
    S_OK
}

unsafe extern "system" fn faulted(exception: *const ExceptionPointers) -> i32 {
    let record = &*(*exception).record;
    let reading = if record.parameters >= 2 {
        record.information[1]
    } else {
        0
    };
    let on_target = record.code == EXCEPTION_ACCESS_VIOLATION
        && record.parameters >= 2
        && reading == FOREIGN_TARGET.load(Ordering::SeqCst);

    println!("fault {:08x} reading {:x}", record.code, reading);
    ExitProcess(if on_target {
        FAULTED_ON_THE_FOREIGN_TARGET
    } else {
        FAULTED_ELSEWHERE
    });
}

fn hex(text: &str) -> usize {
    usize::from_str_radix(text, 16).unwrap_or(0)
}

fn revoke(window: &str, target: &str) -> i32 {
    FOREIGN_TARGET.store(hex(target), Ordering::SeqCst);
    unsafe {
        SetUnhandledExceptionFilter(faulted);
        OleInitialize(std::ptr::null_mut());
        let result = RevokeDragDrop(hex(window) as Hwnd);
        println!("revoke returned {:08x}", result as u32);
    }
    0
}

unsafe fn unreachable_target() -> *mut DropTarget {
    for place in [
        0x1000_0000_0000_usize,
        0x2000_0000_0000,
        0x3000_0000_0000,
        0x4000_0000_0000,
    ] {
        let target = VirtualAlloc(
            place as *mut c_void,
            std::mem::size_of::<DropTarget>(),
            MEM_RESERVE | MEM_COMMIT,
            PAGE_READWRITE,
        )
        .cast::<DropTarget>();
        if !target.is_null() {
            (*target).methods = &METHODS;
            return target;
        }
    }

    std::ptr::null_mut()
}

unsafe fn wait_pumping(process: Handle) -> u32 {
    let deadline = GetTickCount64() + 60000;

    loop {
        let now = GetTickCount64();
        if now >= deadline {
            return WAIT_TIMEOUT;
        }

        let woke = MsgWaitForMultipleObjects(1, &process, 0, (deadline - now) as u32, QS_ALLINPUT);
        if woke != WAIT_OBJECT_0 + 1 {
            return woke;
        }

        let mut message = Message([0; 64]);
        while PeekMessageA(&mut message, std::ptr::null_mut(), 0, 0, PM_REMOVE) != 0 {
            DispatchMessageA(&message);
        }
    }
}

fn hold(this: &str) -> i32 {
    unsafe {
        OleInitialize(std::ptr::null_mut());

        let window = CreateWindowExA(
            0,
            c"STATIC".as_ptr().cast(),
            c"drag-drop probe".as_ptr().cast(),
            0,
            0,
            0,
            0,
            0,
            HWND_MESSAGE as Hwnd,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
        );
        let target = unreachable_target();
        if window.is_null() || target.is_null() {
            println!("probe could not set up a window and an unreachable drop target");
            return 1;
        }

        let registered = RegisterDragDrop(window, target);
        if registered < 0 {
            println!(
                "probe could not register drag-and-drop: {:08x}",
                registered as u32
            );
            return 1;
        }

        let command = CString::new(format!(
            "\"{this}\" revoke {:x} {:x}",
            window as usize, target as usize
        ))
        .expect("the command holds no NUL");
        let mut command = command.into_bytes_with_nul();
        let mut startup = StartupInfo {
            size: std::mem::size_of::<StartupInfo>() as u32,
            rest: [0; 100],
        };
        let mut child = ProcessInformation {
            process: std::ptr::null_mut(),
            thread: std::ptr::null_mut(),
            process_id: 0,
            thread_id: 0,
        };
        if CreateProcessA(
            std::ptr::null(),
            command.as_mut_ptr(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            1,
            0,
            std::ptr::null_mut(),
            std::ptr::null(),
            &mut startup,
            &mut child,
        ) == 0
        {
            println!(
                "probe could not start the revoking process: {}",
                GetLastError()
            );
            return 1;
        }

        if wait_pumping(child.process) != WAIT_OBJECT_0 {
            TerminateProcess(child.process, 1);
            println!("revoke hung");
            return 0;
        }

        let mut code = 0;
        GetExitCodeProcess(child.process, &mut code);
        if code == FAULTED_ON_THE_FOREIGN_TARGET {
            println!("revoke crashed");
            return 0;
        }

        println!("revoke survived with exit code {code}");
    }
    0
}

fn main() {
    let arguments: Vec<String> = std::env::args().collect();
    exit(match arguments.as_slice() {
        [_, verb, window, target] if verb == "revoke" => revoke(window, target),
        _ => hold(&arguments[0]),
    });
}
