import ctypes
import sys

LIBRARY, *ARGUMENTS = sys.argv[1:]

render = ctypes.CDLL(LIBRARY).audio_render
render.argtypes = [ctypes.c_char_p] * 4
render.restype = ctypes.c_int
sys.exit(render(*(argument.encode() for argument in ARGUMENTS)))
