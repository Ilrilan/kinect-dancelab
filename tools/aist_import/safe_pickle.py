"""Безопасная загрузка pickle по белому списку (numpy, scipy.sparse, заглушки chumpy)."""
import io
import pickle
import sys
from collections import OrderedDict

import numpy as np
import scipy.sparse as sp



class ChStub:
    def __setstate__(self, state):
        self.state = state

    def value(self):
        s = self.state
        if isinstance(s, dict) and "x" in s:
            return np.asarray(s["x"])
        return s


ALLOWED = {
    ("numpy._core.multiarray", "_reconstruct"), ("numpy.core.multiarray", "_reconstruct"),
    ("numpy._core.multiarray", "scalar"), ("numpy.core.multiarray", "scalar"),
    ("numpy", "ndarray"), ("numpy", "dtype"),
    ("collections", "OrderedDict"),
}


class Safe(pickle.Unpickler):
    def find_class(self, module, name):
        if (module, name) in ALLOWED:
            return super().find_class(module, name)
        if (module, name) in (("copy_reg", "_reconstructor"), ("copyreg", "_reconstructor")):
            import copyreg
            return copyreg._reconstructor
        if module == "__builtin__" and name in ("object", "set", "frozenset", "dict", "list", "tuple"):
            return {"object": object, "set": set, "frozenset": frozenset, "dict": dict, "list": list, "tuple": tuple}[name]
        if module.startswith("scipy.sparse") and name in ("csc_matrix", "csr_matrix", "coo_matrix"):
            return getattr(sp, name)
        if module.startswith("chumpy"):
            return ChStub
        raise pickle.UnpicklingError(f"blocked: {module}.{name}")


def load(path, encoding="latin1"):
    with open(path, "rb") as f:
        return Safe(io.BytesIO(f.read()), encoding=encoding).load()


def val(x):
    if isinstance(x, ChStub):
        return x.value()
    if sp.issparse(x):
        return x
    return np.asarray(x)

