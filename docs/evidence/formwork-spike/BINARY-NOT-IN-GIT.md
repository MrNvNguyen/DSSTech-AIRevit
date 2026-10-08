# Binary artifacts not in git (formwork spike)

Do not commit:

- Revit models (`.rvt` / `.rte`) used for N0 timing or probe runs
- Exported spike DLLs under evidence (build outputs live under `tools/formwork-spike/FormworkSpike.AddIn/bin/`)

When capturing a measured N0 run, record in `pilot-model-pointer.md` and `evidence-index.json`:

- byte length
- SHA-256 (`certutil -hashfile <path> SHA256` on Windows)
- Revit host year + build
- regenerate command (`tools/formwork-spike/run-spike.ps1 -Install` + External Tools command)

Store large files locally or in PO-approved storage outside this repo.
