# Binary artifacts not in git

Do not commit Revit models (`.rvt`), full G8 project files, or exports > 50 MiB.

When capturing G2 evidence, record in `README.md`:

- byte length
- SHA-256 (`certutil -hashfile <path> SHA256` on Windows)
- regenerate command (Revit selection + export button path)

Store large files locally or in PO-approved storage outside this repo.
