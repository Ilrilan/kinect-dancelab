"""Безопасный осмотр pickle: разрешены только numpy-массивы и простые контейнеры."""
import io
import pickle
import sys

import numpy as np

ALLOWED = {
    ("numpy._core.multiarray", "_reconstruct"),
    ("numpy.core.multiarray", "_reconstruct"),
    ("numpy._core.multiarray", "scalar"),
    ("numpy.core.multiarray", "scalar"),
    ("numpy", "ndarray"),
    ("numpy", "dtype"),
    ("collections", "OrderedDict"),
    ("builtins", "set"),
}


class Safe(pickle.Unpickler):
    def find_class(self, module, name):
        if (module, name) in ALLOWED:
            return super().find_class(module, name)
        raise pickle.UnpicklingError(f"blocked: {module}.{name}")


def describe(obj, indent=0):
    pad = " " * indent
    if isinstance(obj, dict):
        for k, v in obj.items():
            if isinstance(v, np.ndarray):
                print(f"{pad}{k}: ndarray shape={v.shape} dtype={v.dtype} min={v.min():.4f} max={v.max():.4f}")
            elif isinstance(v, dict):
                print(f"{pad}{k}: dict")
                describe(v, indent + 2)
            else:
                print(f"{pad}{k}: {type(v).__name__} = {str(v)[:80]}")
    else:
        print(f"{pad}{type(obj).__name__}")


path = sys.argv[1]
with open(path, "rb") as f:
    data = Safe(io.BytesIO(f.read())).load()
print(path.split("/")[-1])
describe(data)
if len(sys.argv) > 2 and sys.argv[2] == "dump":
    for k, v in data.items():
        if isinstance(v, np.ndarray):
            print(k, "first frame:")
            print(np.array2string(v[0], precision=4, suppress_small=True, max_line_width=120))
            if v.shape[0] > 1:
                print(k, "second frame:")
                print(np.array2string(v[1], precision=4, suppress_small=True, max_line_width=120))
