"""Independent reference for Singularity.Kernel test vectors (CounterRng, StateHash).

Re-implements the byte-level spec in Python so the C# vectors are not just
"whatever the C# code printed". Run: python3 tools/reference/kernel_vectors.py
"""
import hashlib
import struct

M64 = (1 << 64) - 1
M32 = (1 << 32) - 1

# SHA-512 initial hash values, one per key field: seed, tick, system, entity, n.
SALTS = [0x6A09E667F3BCC908, 0xBB67AE8584CAA73B, 0x3C6EF372FE94F82B,
         0xA54FF53A5F1D36F1, 0x510E527FADE682D1]


def mix(x):
    z = (x + 0x9E3779B97F4A7C15) & M64
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & M64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & M64
    return z ^ (z >> 31)


def counter_rng(seed, tick, system, entity, n):
    h = mix((seed & M64) ^ SALTS[0])
    h = mix(h ^ (tick & M64) ^ SALTS[1])
    h = mix(h ^ (system & M32) ^ SALTS[2])
    h = mix(h ^ (entity & M32) ^ SALTS[3])
    return mix(h ^ (n & M32) ^ SALTS[4])


RNG_KEYS = [
    (0, 0, 0, 0, 0), (1, 0, 0, 0, 0), (0, 1, 0, 0, 0), (0, 0, 1, 0, 0),
    (0, 0, 0, 1, 0), (0, 0, 0, 0, 1), (42, 1910, 3, 17, 2), (42, 17, 3, 1910, 2),
    (M64, -1, -1, -1, -1), (20261102, 69400, 7, 199, 3),
    (123456789, 365, 1, 0, 0), (123456789, 0, 1, 365, 0),
]


def state_hash(schema_version, tick, n_rows, columns):
    """columns: list of ('q' | 'i', values) in registration order."""
    data = struct.pack('<iqi', schema_version, tick, n_rows)
    for fmt, values in columns:
        data += b''.join(struct.pack('<' + fmt, v) for v in values)
    return hashlib.sha256(data).hexdigest()


if __name__ == '__main__':
    print('mix(0) = 0x%016X' % mix(0))
    for key in RNG_KEYS:
        v = counter_rng(*key)
        print(key, '0x%016XUL' % v, repr((v >> 11) * 2.0 ** -53))
    print('fixed table:', state_hash(1, 1910, 4, [
        ('q', [0, 1, -1, 9223372036854775807]),
        ('i', [0, 1, -1, -2147483648]),
    ]))
    print('empty table:', state_hash(1, 0, 0, []))
