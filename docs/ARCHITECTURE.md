# QS-BIM Norm Selection Engine architecture

```text
Revit Element(s)
   |
   v
RevitElementFactsFactory
   |-- category / family / type
   |-- native geometry / quantity
   |-- Shared Parameters (QS_*)
   v
ElementFacts (canonical units)
   |
   v
WorkItemClassifier
   |-- BIM category mapping
   |-- material/system heuristic
   |-- optional QS_WorkItemId override
   v
WorkItem
   |
   v
NormSelectionEngine
   |-- method compatibility
   |-- category compatibility
   |-- norm_condition evaluation
   |-- exclusive condition groups
   v
Norm Variant / leaf-code
   |
   +--> adjustment_rule
   |
   v
ResourceCalculator
   |-- norm_resource
   |-- quantity unit conversion
   |-- adjustment multipliers/removals
   v
VL / NC / M demand
   |
   v
Results UI -> CSV / JSON
```

## Master vs runtime data

- **SQLite** is the master database and is edited/QC'd by QS/BIM data governance.
- `Data/norm-engine-runtime.json` is a generated read-only runtime cache consumed by the Revit add-in.
- Regenerate runtime cache using `scripts/export-runtime-data.py` after any database update.

## Selection statuses

- `SelectedApproved`: resource status is approved/ready.
- `SelectedQc1`: leaf code and resources exist, but QS review is still required.
- `SelectedCodeOnly`: code can be proposed, resources are not production-ready.
- `MissingParameters`: required selector data is absent.
- `Ambiguous`: more than one leaf code still matches.
- `NoNormMatched`: facts do not match digitized leaf rules.
- `NoWorkItemMapping`: Revit object could not be classified.

The UI must not treat `SelectedQc1` as an officially issuable estimate.
