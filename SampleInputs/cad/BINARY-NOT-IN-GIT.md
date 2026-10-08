# Sample CAD fixtures (pointers)

| File | Bytes | Notes |
|---|---|---|
| `pilot-floor-good.dxf` | small ASCII | INSUNITS=4, column circle, beam line, closed slab |

Large `.dwg` / `.rvt` must **not** be committed. Record SHA locally after generate:

```powershell
Get-FileHash SampleInputs\cad\pilot-floor-good.dxf -Algorithm SHA256
```
