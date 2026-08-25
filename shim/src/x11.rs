use crate::DESKTOP_TITLE;
use std::os::raw::{c_char, c_int, c_short, c_uchar, c_uint, c_ulong, c_ushort, c_void};
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
type Flush = unsafe extern "C" fn(*mut Display) -> c_int;

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
    flush: Flush,
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
                flush: library.symbol(b"XFlush\0")?,
                _library: library,
            })
        }
    }

    unsafe fn hide_tree(
        &self,
        display: *mut Display,
        window: Window,
        opacity: Atom,
        cardinal: Atom,
        shape: Option<&Shape>,
        depth: u8,
    ) {
        if depth > 16 {
            return;
        }

        let mut title = ptr::null_mut();

        if (self.fetch_name)(display, window, &mut title) != 0 && !title.is_null() {
            if std::ffi::CStr::from_ptr(title).to_bytes() == DESKTOP_TITLE.as_bytes() {
                let value = 0u32;

                (self.change_property)(
                    display,
                    window,
                    opacity,
                    cardinal,
                    32,
                    PROP_MODE_REPLACE,
                    (&value as *const u32).cast(),
                    1,
                );

                if let Some(shape) = shape {
                    shape.hide_input(display, window);
                }
            }

            (self.free)(title.cast());
        }

        let mut root = 0;
        let mut parent = 0;
        let mut children = ptr::null_mut();
        let mut count = 0;

        if (self.query_tree)(
            display,
            window,
            &mut root,
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
            (self.free)(children_ptr.cast());
        }

        for child in children {
            self.hide_tree(display, child, opacity, cardinal, shape, depth + 1);
        }
    }
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

fn watch(stop: Arc<AtomicBool>) {
    let Some(x11) = X11::load() else {
        return;
    };
    let shape = Shape::load();

    unsafe {
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
    let opacity = (x11.intern_atom)(display, b"_NET_WM_WINDOW_OPACITY\0".as_ptr().cast(), 0);
    let cardinal = (x11.intern_atom)(display, b"CARDINAL\0".as_ptr().cast(), 0);

    if opacity == 0 || cardinal == 0 {
        return;
    }

    while !stop.load(Ordering::Relaxed) {
        x11.hide_tree(display, root, opacity, cardinal, shape, 0);
        (x11.flush)(display);
        thread::sleep(Duration::from_millis(250));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn desktop_title_is_the_wine_title() {
        assert_eq!(DESKTOP_TITLE, "Wine Desktop");
    }
}
