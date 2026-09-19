use crate::DESKTOP_TITLE;
use std::os::raw::{c_char, c_int, c_long, c_short, c_uchar, c_uint, c_ulong, c_ushort, c_void};
use std::ptr;
use std::sync::{
    atomic::{AtomicBool, Ordering},
    Arc,
};
use std::thread::{self, JoinHandle};
use std::time::Duration;

const RTLD_NOW: c_int = 2;
const PROP_MODE_REPLACE: c_int = 0;
const SHAPE_INPUT: c_int = 2;
const SHAPE_SET: c_int = 0;
const Y_BANDED: c_int = 0;

#[repr(C)]
struct Rectangle {
    x: c_short,
    y: c_short,
    width: c_ushort,
    height: c_ushort,
}

#[link(name = "dl")]
extern "C" {
    fn dlopen(file: *const c_char, flags: c_int) -> *mut c_void;
    fn dlclose(handle: *mut c_void) -> c_int;
    fn dlsym(handle: *mut c_void, name: *const c_char) -> *mut c_void;
}

struct Library(*mut c_void);

impl Library {
    fn open(name: &[u8]) -> Option<Self> {
        let handle = unsafe { dlopen(name.as_ptr().cast(), RTLD_NOW) };

        (!handle.is_null()).then_some(Self(handle))
    }

    unsafe fn symbol<T: Copy>(&self, name: &[u8]) -> Option<T> {
        let symbol = dlsym(self.0, name.as_ptr().cast());

        (!symbol.is_null()).then(|| std::mem::transmute_copy(&symbol))
    }
}

impl Drop for Library {
    fn drop(&mut self) {
        unsafe {
            dlclose(self.0);
        }
    }
}

type Display = c_void;
type Window = c_ulong;
type Atom = c_ulong;

type OpenDisplay = unsafe extern "C" fn(*const c_char) -> *mut Display;
type CloseDisplay = unsafe extern "C" fn(*mut Display);
type DefaultRootWindow = unsafe extern "C" fn(*mut Display) -> Window;
type QueryTree = unsafe extern "C" fn(
    *mut Display,
    Window,
    *mut Window,
    *mut Window,
    *mut *mut Window,
    *mut c_uint,
) -> c_int;
type FetchName = unsafe extern "C" fn(*mut Display, Window, *mut *mut c_char) -> c_int;
type Free = unsafe extern "C" fn(*mut c_void) -> c_int;
type InternAtom = unsafe extern "C" fn(*mut Display, *const c_char, c_int) -> Atom;
type ChangeProperty =
    unsafe extern "C" fn(*mut Display, Window, Atom, Atom, c_int, c_int, *const c_uchar, c_int);
type SendEvent =
    unsafe extern "C" fn(*mut Display, Window, c_int, c_long, *mut ClientMessage) -> c_int;
type Flush = unsafe extern "C" fn(*mut Display) -> c_int;
type SelectInput = unsafe extern "C" fn(*mut Display, Window, c_long) -> c_int;
type Pending = unsafe extern "C" fn(*mut Display) -> c_int;
type NextEvent = unsafe extern "C" fn(*mut Display, *mut Event) -> c_int;
type ConnectionNumber = unsafe extern "C" fn(*mut Display) -> c_int;
type ErrorHandler = unsafe extern "C" fn(*mut Display, *mut c_void) -> c_int;
type SetErrorHandler = unsafe extern "C" fn(ErrorHandler) -> Option<ErrorHandler>;

unsafe extern "C" fn ignore_error(_: *mut Display, _: *mut c_void) -> c_int {
    0
}

const CREATE_NOTIFY: c_int = 16;
const DESTROY_NOTIFY: c_int = 17;
const MAP_NOTIFY: c_int = 19;
const REPARENT_NOTIFY: c_int = 21;
const PROPERTY_NOTIFY: c_int = 28;
const CLIENT_MESSAGE: c_int = 33;
const STRUCTURE_NOTIFY: c_long = 1 << 17;
const SUBSTRUCTURE_NOTIFY: c_long = 1 << 19;
const SUBSTRUCTURE_REDIRECT: c_long = 1 << 20;
const PROPERTY_CHANGE: c_long = 1 << 22;
const WATCHED: c_long = STRUCTURE_NOTIFY | PROPERTY_CHANGE;
const WM_NAME: Atom = 39;
const PROPERTY_DELETE: c_int = 1;
const NET_WM_STATE_ADD: c_long = 1;
const IDLE_WAIT: Duration = Duration::from_millis(250);

#[repr(C)]
struct Event {
    kind: c_int,
    serial: c_ulong,
    send_event: c_int,
    display: *mut Display,
    first: c_ulong,
    second: c_ulong,
    third: c_ulong,
    fourth: c_int,
    _pad: [c_long; 16],
}

#[repr(C)]
struct ClientMessage {
    kind: c_int,
    serial: c_ulong,
    send_event: c_int,
    display: *mut Display,
    window: Window,
    message_type: Atom,
    format: c_int,
    data: [c_long; 5],
    _pad: [c_long; 12],
}

struct X11 {
    _library: Library,
    open_display: OpenDisplay,
    close_display: CloseDisplay,
    default_root_window: DefaultRootWindow,
    query_tree: QueryTree,
    fetch_name: FetchName,
    free: Free,
    intern_atom: InternAtom,
    change_property: ChangeProperty,
    send_event: SendEvent,
    flush: Flush,
    set_error_handler: SetErrorHandler,
    select_input: SelectInput,
    pending: Pending,
    next_event: NextEvent,
    connection_number: ConnectionNumber,
}

impl X11 {
    fn load() -> Option<Self> {
        let library = Library::open(b"libX11.so.6\0")?;

        unsafe {
            Some(Self {
                open_display: library.symbol(b"XOpenDisplay\0")?,
                close_display: library.symbol(b"XCloseDisplay\0")?,
                default_root_window: library.symbol(b"XDefaultRootWindow\0")?,
                query_tree: library.symbol(b"XQueryTree\0")?,
                fetch_name: library.symbol(b"XFetchName\0")?,
                free: library.symbol(b"XFree\0")?,
                intern_atom: library.symbol(b"XInternAtom\0")?,
                change_property: library.symbol(b"XChangeProperty\0")?,
                send_event: library.symbol(b"XSendEvent\0")?,
                flush: library.symbol(b"XFlush\0")?,
                set_error_handler: library.symbol(b"XSetErrorHandler\0")?,
                select_input: library.symbol(b"XSelectInput\0")?,
                pending: library.symbol(b"XPending\0")?,
                next_event: library.symbol(b"XNextEvent\0")?,
                connection_number: library.symbol(b"XConnectionNumber\0")?,
                _library: library,
            })
        }
    }
}

struct Atoms {
    opacity: Atom,
    cardinal: Atom,
    net_wm_state: Atom,
    atom: Atom,
    skip_taskbar: Atom,
    skip_pager: Atom,
}

type ShapeQueryExtension = unsafe extern "C" fn(*mut Display, *mut c_int, *mut c_int) -> c_int;
type ShapeCombineRectangles = unsafe extern "C" fn(
    *mut Display,
    Window,
    c_int,
    c_int,
    c_int,
    *const Rectangle,
    c_int,
    c_int,
    c_int,
);

struct Shape {
    _library: Library,
    query_extension: ShapeQueryExtension,
    combine_rectangles: ShapeCombineRectangles,
}

impl Shape {
    fn load() -> Option<Self> {
        let library = Library::open(b"libXext.so.6\0")?;

        unsafe {
            Some(Self {
                query_extension: library.symbol(b"XShapeQueryExtension\0")?,
                combine_rectangles: library.symbol(b"XShapeCombineRectangles\0")?,
                _library: library,
            })
        }
    }

    unsafe fn available(&self, display: *mut Display) -> bool {
        let mut event_base = 0;
        let mut error_base = 0;

        (self.query_extension)(display, &mut event_base, &mut error_base) != 0
    }

    unsafe fn hide_input(&self, display: *mut Display, window: Window) {
        let empty = Rectangle {
            x: 0,
            y: 0,
            width: 0,
            height: 0,
        };

        (self.combine_rectangles)(
            display,
            window,
            SHAPE_INPUT,
            0,
            0,
            &empty,
            1,
            SHAPE_SET,
            Y_BANDED,
        );
    }
}

pub struct Watcher {
    stop: Arc<AtomicBool>,
    thread: Option<JoinHandle<()>>,
}

impl Watcher {
    pub fn start() -> Self {
        let stop = Arc::new(AtomicBool::new(false));
        let signal = Arc::clone(&stop);
        let thread = thread::spawn(move || watch(signal));

        Self {
            stop,
            thread: Some(thread),
        }
    }
}

impl Drop for Watcher {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);

        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

struct Hider<'a> {
    x11: &'a X11,
    display: *mut Display,
    root: Window,
    atoms: Atoms,
    shape: Option<&'a Shape>,
    hidden: Vec<Window>,
}

impl Hider<'_> {
    unsafe fn walk(&mut self, window: Window, depth: u8) {
        if depth > 16 {
            return;
        }

        self.inspect(window);

        let mut tree_root = 0;
        let mut parent = 0;
        let mut children = ptr::null_mut();
        let mut count = 0;

        if (self.x11.query_tree)(
            self.display,
            window,
            &mut tree_root,
            &mut parent,
            &mut children,
            &mut count,
        ) == 0
        {
            return;
        }

        let children_ptr = children;
        let children = if children_ptr.is_null() {
            Vec::new()
        } else {
            std::slice::from_raw_parts(children_ptr, count as usize).to_vec()
        };

        if !children_ptr.is_null() {
            (self.x11.free)(children_ptr.cast());
        }

        for child in children {
            self.walk(child, depth + 1);
        }
    }

    unsafe fn handle(&mut self, event: &Event) {
        match event.kind {
            CREATE_NOTIFY => self.watch(event.second),
            REPARENT_NOTIFY if event.third == self.root => self.watch(event.second),
            PROPERTY_NOTIFY if event.second == WM_NAME => self.inspect(event.first),
            PROPERTY_NOTIFY
                if event.second == self.atoms.opacity
                    && event.fourth == PROPERTY_DELETE
                    && self.hidden.contains(&event.first) =>
            {
                self.hide(event.first)
            }
            MAP_NOTIFY if self.hidden.contains(&event.second) => self.hide(event.second),
            DESTROY_NOTIFY => self.hidden.retain(|window| *window != event.second),
            _ => {}
        }
    }

    unsafe fn watch(&mut self, window: Window) {
        (self.x11.select_input)(self.display, window, WATCHED);
        self.inspect(window);
    }

    unsafe fn inspect(&mut self, window: Window) {
        if !self.titled_desktop(window) {
            return;
        }

        if !self.hidden.contains(&window) {
            self.hidden.push(window);
            (self.x11.select_input)(self.display, window, WATCHED);
        }

        self.hide(window);
    }

    unsafe fn titled_desktop(&self, window: Window) -> bool {
        let mut title = ptr::null_mut();

        if (self.x11.fetch_name)(self.display, window, &mut title) == 0 || title.is_null() {
            return false;
        }

        let desktop = std::ffi::CStr::from_ptr(title).to_bytes() == DESKTOP_TITLE.as_bytes();
        (self.x11.free)(title.cast());

        desktop
    }

    unsafe fn hide(&self, window: Window) {
        let (x11, display, atoms) = (self.x11, self.display, &self.atoms);
        let value = 0u32;

        (x11.change_property)(
            display,
            window,
            atoms.opacity,
            atoms.cardinal,
            32,
            PROP_MODE_REPLACE,
            (&value as *const u32).cast(),
            1,
        );

        if let Some(shape) = self.shape {
            shape.hide_input(display, window);
        }

        let skip = [atoms.skip_taskbar, atoms.skip_pager];

        (x11.change_property)(
            display,
            window,
            atoms.net_wm_state,
            atoms.atom,
            32,
            PROP_MODE_REPLACE,
            skip.as_ptr().cast(),
            skip.len() as c_int,
        );

        let mut message = ClientMessage {
            kind: CLIENT_MESSAGE,
            serial: 0,
            send_event: 1,
            display,
            window,
            message_type: atoms.net_wm_state,
            format: 32,
            data: [
                NET_WM_STATE_ADD,
                atoms.skip_taskbar as c_long,
                atoms.skip_pager as c_long,
                1,
                0,
            ],
            _pad: [0; 12],
        };

        (x11.send_event)(
            display,
            self.root,
            0,
            SUBSTRUCTURE_REDIRECT | SUBSTRUCTURE_NOTIFY,
            &mut message,
        );
    }
}

fn watch(stop: Arc<AtomicBool>) {
    let Some(x11) = X11::load() else {
        return;
    };
    let shape = Shape::load();

    unsafe {
        (x11.set_error_handler)(ignore_error);

        let display = (x11.open_display)(ptr::null());

        if display.is_null() {
            return;
        }

        let shape = shape.filter(|shape| shape.available(display));

        watch_display(&x11, display, shape.as_ref(), stop);
        (x11.close_display)(display);
    }
}

unsafe fn watch_display(
    x11: &X11,
    display: *mut Display,
    shape: Option<&Shape>,
    stop: Arc<AtomicBool>,
) {
    let root = (x11.default_root_window)(display);
    let atoms = Atoms {
        opacity: (x11.intern_atom)(display, b"_NET_WM_WINDOW_OPACITY\0".as_ptr().cast(), 0),
        cardinal: (x11.intern_atom)(display, b"CARDINAL\0".as_ptr().cast(), 0),
        net_wm_state: (x11.intern_atom)(display, b"_NET_WM_STATE\0".as_ptr().cast(), 0),
        atom: (x11.intern_atom)(display, b"ATOM\0".as_ptr().cast(), 0),
        skip_taskbar: (x11.intern_atom)(
            display,
            b"_NET_WM_STATE_SKIP_TASKBAR\0".as_ptr().cast(),
            0,
        ),
        skip_pager: (x11.intern_atom)(display, b"_NET_WM_STATE_SKIP_PAGER\0".as_ptr().cast(), 0),
    };

    if atoms.opacity == 0
        || atoms.cardinal == 0
        || atoms.net_wm_state == 0
        || atoms.atom == 0
        || atoms.skip_taskbar == 0
        || atoms.skip_pager == 0
    {
        return;
    }

    let connection = (x11.connection_number)(display);
    let mut hider = Hider {
        x11,
        display,
        root,
        atoms,
        shape,
        hidden: Vec::new(),
    };

    (x11.select_input)(display, root, SUBSTRUCTURE_NOTIFY);
    hider.walk(root, 0);

    while !stop.load(Ordering::Relaxed) {
        while (x11.pending)(display) > 0 {
            let mut event = std::mem::zeroed::<Event>();
            (x11.next_event)(display, &mut event);
            hider.handle(&event);
        }

        (x11.flush)(display);
        crate::session::wait_readable(&[connection], Some(IDLE_WAIT));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn an_x_error_never_ends_the_process() {
        let handler: ErrorHandler = ignore_error;

        assert_eq!(unsafe { handler(ptr::null_mut(), ptr::null_mut()) }, 0);
    }

    #[test]
    fn desktop_title_is_the_wine_title() {
        assert_eq!(DESKTOP_TITLE, "Wine Desktop");
    }

    #[test]
    fn a_client_message_matches_what_xlib_reads() {
        assert_eq!(std::mem::size_of::<ClientMessage>(), 192);
        assert_eq!(std::mem::offset_of!(ClientMessage, kind), 0);
        assert_eq!(std::mem::offset_of!(ClientMessage, serial), 8);
        assert_eq!(std::mem::offset_of!(ClientMessage, send_event), 16);
        assert_eq!(std::mem::offset_of!(ClientMessage, display), 24);
        assert_eq!(std::mem::offset_of!(ClientMessage, window), 32);
        assert_eq!(std::mem::offset_of!(ClientMessage, message_type), 40);
        assert_eq!(std::mem::offset_of!(ClientMessage, format), 48);
        assert_eq!(std::mem::offset_of!(ClientMessage, data), 56);
    }

    #[test]
    fn an_event_matches_what_xlib_writes() {
        assert_eq!(std::mem::size_of::<Event>(), 192);
        assert_eq!(std::mem::offset_of!(Event, kind), 0);
        assert_eq!(std::mem::offset_of!(Event, serial), 8);
        assert_eq!(std::mem::offset_of!(Event, send_event), 16);
        assert_eq!(std::mem::offset_of!(Event, display), 24);
        assert_eq!(std::mem::offset_of!(Event, first), 32);
        assert_eq!(std::mem::offset_of!(Event, second), 40);
        assert_eq!(std::mem::offset_of!(Event, third), 48);
        assert_eq!(std::mem::offset_of!(Event, fourth), 56);
    }
}
