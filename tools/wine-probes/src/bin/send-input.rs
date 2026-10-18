#![windows_subsystem = "windows"]

use std::ffi::c_void;
use std::os::windows::ffi::OsStrExt;
use std::process::exit;
use std::thread::sleep;
use std::time::Duration;

type Hwnd = *mut c_void;

const SW_RESTORE: i32 = 9;
const SM_XVIRTUALSCREEN: i32 = 76;
const SM_YVIRTUALSCREEN: i32 = 77;
const SM_CXVIRTUALSCREEN: i32 = 78;
const SM_CYVIRTUALSCREEN: i32 = 79;
const INPUT_MOUSE: u32 = 0;
const INPUT_KEYBOARD: u32 = 1;
const MOUSEEVENTF_MOVE: u32 = 0x0001;
const MOUSEEVENTF_LEFTDOWN: u32 = 0x0002;
const MOUSEEVENTF_LEFTUP: u32 = 0x0004;
const MOUSEEVENTF_VIRTUALDESK: u32 = 0x4000;
const MOUSEEVENTF_ABSOLUTE: u32 = 0x8000;
const KEYEVENTF_KEYUP: u32 = 0x0002;
const KEYEVENTF_UNICODE: u32 = 0x0004;

#[repr(C)]
#[derive(Clone, Copy, Default)]
struct Point {
    x: i32,
    y: i32,
}

#[repr(C)]
#[derive(Default)]
struct Rect {
    left: i32,
    top: i32,
    right: i32,
    bottom: i32,
}

#[repr(C)]
#[derive(Clone, Copy)]
struct MouseInput {
    dx: i32,
    dy: i32,
    mouse_data: u32,
    flags: u32,
    time: u32,
    extra: usize,
}

#[repr(C)]
#[derive(Clone, Copy)]
struct KeyboardInput {
    virtual_key: u16,
    scan: u16,
    flags: u32,
    time: u32,
    extra: usize,
}

#[repr(C)]
#[derive(Clone, Copy)]
union Event {
    mouse: MouseInput,
    keyboard: KeyboardInput,
}

#[repr(C)]
#[derive(Clone, Copy)]
struct Input {
    kind: u32,
    event: Event,
}

#[link(name = "user32")]
extern "system" {
    fn FindWindowW(class: *const u16, title: *const u16) -> Hwnd;
    fn IsIconic(window: Hwnd) -> i32;
    fn ShowWindow(window: Hwnd, show: i32) -> i32;
    fn SetForegroundWindow(window: Hwnd) -> i32;
    fn GetForegroundWindow() -> Hwnd;
    fn GetClientRect(window: Hwnd, rect: *mut Rect) -> i32;
    fn PtInRect(rect: *const Rect, point: Point) -> i32;
    fn ClientToScreen(window: Hwnd, point: *mut Point) -> i32;
    fn WindowFromPoint(point: Point) -> Hwnd;
    fn IsChild(parent: Hwnd, window: Hwnd) -> i32;
    fn GetSystemMetrics(index: i32) -> i32;
    fn SendInput(count: u32, inputs: *const Input, size: i32) -> u32;
}

fn mouse(dx: i32, dy: i32, flags: u32) -> Input {
    Input {
        kind: INPUT_MOUSE,
        event: Event {
            mouse: MouseInput {
                dx,
                dy,
                mouse_data: 0,
                flags,
                time: 0,
                extra: 0,
            },
        },
    }
}

fn key(scan: u16, flags: u32) -> Input {
    Input {
        kind: INPUT_KEYBOARD,
        event: Event {
            keyboard: KeyboardInput {
                virtual_key: 0,
                scan,
                flags,
                time: 0,
                extra: 0,
            },
        },
    }
}

fn send(inputs: &[Input]) -> bool {
    let size = std::mem::size_of::<Input>() as i32;
    unsafe { SendInput(inputs.len() as u32, inputs.as_ptr(), size) as usize == inputs.len() }
}

fn number(argument: &std::ffi::OsStr) -> i32 {
    let text = argument.to_string_lossy();
    let digits: String = text
        .trim_start()
        .chars()
        .enumerate()
        .take_while(|(index, character)| {
            character.is_ascii_digit() || (*index == 0 && "+-".contains(*character))
        })
        .map(|(_, character)| character)
        .collect();
    digits.parse().unwrap_or(0)
}

fn main() {
    let arguments: Vec<_> = std::env::args_os().collect();
    if arguments.len() != 5 {
        exit(2);
    }

    let input = std::fs::read(&arguments[1]);
    let title: Vec<u16> = arguments[2].encode_wide().chain([0]).collect();
    let window = unsafe { FindWindowW(std::ptr::null(), title.as_ptr()) };
    let (x, y) = (number(&arguments[3]), number(&arguments[4]));
    let Ok(input) = input else { exit(3) };

    if window.is_null() {
        exit(4);
    }

    unsafe {
        if IsIconic(window) != 0 {
            ShowWindow(window, SW_RESTORE);
        }
        SetForegroundWindow(window);
        for _ in 0..20 {
            if GetForegroundWindow() == window {
                break;
            }
            sleep(Duration::from_millis(10));
        }
        if GetForegroundWindow() != window {
            exit(5);
        }
        sleep(Duration::from_millis(50));
        if GetForegroundWindow() != window {
            exit(6);
        }

        let mut point = Point { x, y };
        let mut client = Rect::default();
        if GetClientRect(window, &mut client) == 0
            || PtInRect(&client, point) == 0
            || ClientToScreen(window, &mut point) == 0
        {
            exit(7);
        }
        let hit = WindowFromPoint(point);
        if hit != window && IsChild(window, hit) == 0 {
            exit(8);
        }

        let left = GetSystemMetrics(SM_XVIRTUALSCREEN);
        let top = GetSystemMetrics(SM_YVIRTUALSCREEN);
        let width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        let height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if width <= 0 || height <= 0 {
            exit(9);
        }

        let dx = ((2 * i64::from(point.x - left) + 1) * 65536 / (2 * i64::from(width))) as i32;
        let dy = ((2 * i64::from(point.y - top) + 1) * 65536 / (2 * i64::from(height))) as i32;
        let click = [
            mouse(
                dx,
                dy,
                MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
            ),
            mouse(0, 0, MOUSEEVENTF_LEFTDOWN),
            mouse(0, 0, MOUSEEVENTF_LEFTUP),
        ];
        if !send(&click) {
            exit(10);
        }
    }

    for pair in input.chunks_exact(2) {
        let character = u16::from_le_bytes([pair[0], pair[1]]);
        if !send(&[
            key(character, KEYEVENTF_UNICODE),
            key(character, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP),
        ]) {
            exit(11);
        }
        sleep(Duration::from_millis(50));
    }
}
