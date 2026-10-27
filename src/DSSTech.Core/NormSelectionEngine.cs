namespace DSSTech.Core;

public sealed class NormSelectionEngine
{
    private readonly RuntimeData _data;
    private readonly EngineConfig _config;
    private readonly WorkItemClassifier _classifier;
    private readonly ResourceCalculator _resourceCalculator;
    private readonly Dictionary<string, NormIndexRecord> _indexByCode;
    private readonly Dictionary<string, List<NormCondition>> _conditionsByCode;
    private readonly Dictionary<string, WorkFamily> _familyById;

    public NormSelectionEngine(RuntimeData data, EngineConfig? config = null)
    {
        _data = data;
        _config = config ?? new EngineConfig();
        _classifier = new WorkItemClassifier(data);
        _resourceCalculator = new ResourceCalculator(data);
        _indexByCode = data.NormIndex.ToDictionary(x => x.NormCode, StringComparer.OrdinalIgnoreCase);
        _conditionsByCode = data.NormConditions.GroupBy(x => x.VariantCode, StringComparer.OrdinalIgnoreCase)
                                                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        _familyById = data.WorkFamilies.ToDictionary(x => x.WorkItemId, StringComparer.OrdinalIgnoreCase);
    }

    public NormSelectionResult Select(ElementFacts element)
    {
        try
        {
            var (workItemId, classConfidence, classWarnings) = _classifier.Classify(element);
            var result = new NormSelectionResult
            {
                ElementId = element.ElementId,
                RevitCategory = element.RevitCategory,
                WorkItemId = workItemId,
                Quantity = element.Quantity,
                QuantityUnit = element.QuantityUnit,
                Confidence = classConfidence,
                Warnings = classWarnings
            };

            if (string.IsNullOrWhiteSpace(workItemId))
            {
                result.Status = SelectionStatus.NoWorkItemMapping;
                result.Warnings.Add($"Không có BIM mapping cho category {element.RevitCategory}.");
                return result;
            }

            var (quantity, quantityUnit) = QuantityResolver.Resolve(_data, element, workItemId);
            element.Quantity = quantity;
            element.QuantityUnit = quantityUnit;
            result.Quantity = quantity;
            result.QuantityUnit = quantityUnit;

            if (_familyById.TryGetValue(workItemId, out var family))
                result.WorkItemName = $"{family.GroupL1} / {family.GroupL2}";

            var facts = new FactBag(element, _config, workItemId);
            NormalizeDerivedFacts(element, workItemId, facts);

            if (_config.RequireExplicitConstructionMethod && workItemId == "XD-CONC" && !facts.TryGet("PlacementMethod", out _))
            {
                result.Status = SelectionStatus.MissingParameters;
                result.MissingParameters = ["PlacementMethod"];
                result.Warnings.Add("Công tác bê tông cần xác định biện pháp đổ: Manual / Crane / Pump.");
                return result;
            }

            if (workItemId == "XD-CONC" && facts.TryGet("PlacementMethod", out var methodValue))
            {
                var method = ValueTools.NormalizeText(methodValue);
                var resourceCriticalMissing = new List<string>();
                if ((method.Contains("manual") || method.Contains("thu cong")) && !facts.TryGet("ConcreteSupply", out _))
                    resourceCriticalMissing.Add("ConcreteSupply");
                if ((method.Contains("pump") || method.Contains("bom")) && !facts.TryGet("PumpType", out _))
                    resourceCriticalMissing.Add("PumpType");
                if (resourceCriticalMissing.Count > 0)
                {
                    result.Status = SelectionStatus.MissingParameters;
                    result.MissingParameters = resourceCriticalMissing;
                    result.Warnings.Add("Thiếu tham số ảnh hưởng trực tiếp đến rule điều chỉnh VL-NC-M; engine dừng trước khi tính resource.");
                    return result;
                }
            }

            var indexedCodes = new HashSet<string>(
                _data.NormIndex.Where(x => x.WorkItemId.Equals(workItemId, StringComparison.OrdinalIgnoreCase)).Select(x => x.NormCode),
                StringComparer.OrdinalIgnoreCase);
            var candidateVariants = _data.NormVariants.Where(v => indexedCodes.Contains(v.VariantCode)).ToList();

            if (candidateVariants.Count == 0)
            {
                result.Status = SelectionStatus.NoNormMatched;
                result.Warnings.Add("WorkItem đã phân loại nhưng chưa có leaf variant trong resource/rule database.");
                result.CandidateCodes = _data.NormIndex.Where(x => x.WorkItemId.Equals(workItemId, StringComparison.OrdinalIgnoreCase))
                                                       .Take(20).Select(x => x.NormCode).ToList();
                return result;
            }

            candidateVariants = candidateVariants.Where(v => MethodCompatible(v, facts) && CategoryCompatible(v, facts)).ToList();
            var evals = new List<CandidateEvaluation>();
            foreach (var variant in candidateVariants)
            {
                if (!_indexByCode.TryGetValue(variant.VariantCode, out var index)) continue;
                _conditionsByCode.TryGetValue(variant.VariantCode, out var cs);
                evals.Add(ConditionEvaluator.EvaluateCandidate(variant, index, cs ?? [], facts));
            }

            var matches = evals.Where(e => e.IsMatch)
                               .OrderByDescending(e => e.TotalConditions)
                               .ThenByDescending(e => e.Score)
                               .ThenBy(e => e.Variant.OptionNo)
                               .ToList();

            if (matches.Count == 0)
            {
                var missing = evals.SelectMany(e => e.Missing).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.MissingParameters = PrioritizeMissing(missing, workItemId);
                result.CandidateCodes = evals.OrderByDescending(e => e.Score).Take(12).Select(e => e.Variant.VariantCode).ToList();
                result.Status = result.MissingParameters.Count > 0 ? SelectionStatus.MissingParameters : SelectionStatus.NoNormMatched;
                result.Warnings.Add(result.Status == SelectionStatus.MissingParameters
                    ? "Thiếu tham số để chọn leaf-code duy nhất. Hãy bổ sung Shared Parameter hoặc project defaults."
                    : "Có dữ liệu đầu vào nhưng không khớp điều kiện leaf-code hiện có.");
                if (workItemId == "XD-CONC"
                    && facts.TryGet("ElementType", out var etNo)
                    && ValueTools.NormalizeText(etNo).Contains("foundation")
                    && facts.TryGet("PlacementMethod", out var pmNo)
                    && ValueTools.NormalizeText(pmNo).Contains("pump"))
                {
                    result.Warnings.Add("Móng (Foundation): mã AF móng dùng Manual/Crane + Width (cm), không dùng Pump. Hãy Gán giá trị từ danh sách.");
                }
                if (workItemId == "XD-CONC" && !facts.TryGet("Width", out _)
                    && facts.TryGet("ElementType", out var etW)
                    && (ValueTools.NormalizeText(etW).Contains("foundation") || ValueTools.NormalizeText(etW).Contains("blinding")))
                {
                    result.MissingParameters = PrioritizeMissing(
                        result.MissingParameters.Concat(["Width"]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        workItemId);
                    if (result.MissingParameters.Count > 0)
                        result.Status = SelectionStatus.MissingParameters;
                }
                return result;
            }

            var bestTotal = matches[0].TotalConditions;
            var bestScore = matches[0].Score;
            var best = matches.Where(x => x.TotalConditions == bestTotal && Math.Abs(x.Score - bestScore) < 1e-9).ToList();
            if (best.Count > 1)
                best = DisambiguateByElementType(best, facts);
            if (best.Count > 1)
            {
                result.Status = SelectionStatus.Ambiguous;
                result.CandidateCodes = best.Select(x => x.Variant.VariantCode).ToList();
                result.Warnings.Add("Nhiều mã cùng thỏa điều kiện. Cần bổ sung selector hoặc ưu tiên rule.");
                return result;
            }

            var selected = best[0];
            result.NormCode = selected.Variant.VariantCode;
            result.NormName = selected.Variant.VariantName;
            result.NormUnit = selected.Variant.Unit;
            result.DigitizationStatus = selected.Index.DigitizationStatus;
            result.Confidence = Math.Min(1.0, (classConfidence + Math.Max(.5, selected.Score)) / 2.0);

            var resourceCount = _data.NormResources.Count(x => x.VariantCode.Equals(result.NormCode, StringComparison.OrdinalIgnoreCase));
            if (resourceCount > 0)
            {
                var (factor, resources, adjustments, warnings) = _resourceCalculator.Calculate(selected.Variant, selected.Index, element, facts);
                result.NormQuantityFactor = factor;
                result.Resources = resources;
                result.Adjustments = adjustments;
                result.Warnings.AddRange(warnings);
            }

            if (workItemId == "XD-CONC" && !facts.TryGet("ConcreteGrade", out _))
                result.Warnings.Add("Chưa có ConcreteGrade: mã định mức có thể chọn được nhưng lớp vật liệu/đơn giá bê tông chưa đủ để lập giá hoàn chỉnh.");

            var status = selected.Index.DigitizationStatus ?? "";
            if (ValueTools.ContainsIgnoreCase(status, "APPROVED") || status.Equals("RESOURCE_READY", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = SelectionStatus.SelectedApproved;
                result.CanIssueOfficially = true;
            }
            else if (resourceCount > 0 && status.StartsWith("RESOURCE", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = SelectionStatus.SelectedQc1;
                result.CanIssueOfficially = false;
                if (!_config.AllowQc1Calculation) result.Resources.Clear();
                result.Warnings.Add("Resource đang ở trạng thái QC; không tự động phát hành hồ sơ chính thức.");
            }
            else
            {
                result.Status = SelectionStatus.SelectedCodeOnly;
                result.CanIssueOfficially = false;
                if (!_config.AllowCodeOnlyResult) result.NormCode = "";
                result.Warnings.Add("Mã đã chọn nhưng resource VL–NC–M chưa đủ trạng thái READY.");
            }

            return result;
        }
        catch (Exception ex)
        {
            return new NormSelectionResult
            {
                ElementId = element.ElementId,
                RevitCategory = element.RevitCategory,
                Status = SelectionStatus.Error,
                Warnings = [$"Engine error: {ex.Message}"]
            };
        }
    }

    public IReadOnlyList<NormSelectionResult> SelectMany(IEnumerable<ElementFacts> elements) => elements.Select(Select).ToList();

    private List<CandidateEvaluation> DisambiguateByElementType(List<CandidateEvaluation> best, FactBag facts)
    {
        if (!facts.TryGet("ElementType", out var et)) return best;
        var e = ValueTools.NormalizeText(et);
        bool IsFootingHost() =>
            e.Contains("foundation") || e.Contains("mong") || e.Contains("blinding") || e.Contains("lot mong");

        if (!IsFootingHost()) return best;

        var withElementType = best.Where(x =>
            _conditionsByCode.TryGetValue(x.Variant.VariantCode, out var cs)
            && cs.Any(c => c.ParamKey.Equals("ElementType", StringComparison.OrdinalIgnoreCase))).ToList();
        if (withElementType.Count == 1) return withElementType;
        if (withElementType.Count > 1) return withElementType;

        var withWidth = best.Where(x =>
            _conditionsByCode.TryGetValue(x.Variant.VariantCode, out var cs)
            && cs.Any(c => c.ParamKey.Equals("Width", StringComparison.OrdinalIgnoreCase))).ToList();
        if (withWidth.Count >= 1) return withWidth;
        return best;
    }

    private static bool MethodCompatible(NormVariant variant, FactBag facts)
    {
        var methodId = variant.MethodId ?? "";
        if (string.IsNullOrWhiteSpace(methodId)) return true;
        if (!facts.TryGet("PlacementMethod", out var value)) return true; // missing method is reported via ambiguity/no-match, not hard-failed here.
        var m = ValueTools.NormalizeText(value);
        if (methodId.Equals("M-CONC-MANUAL", StringComparison.OrdinalIgnoreCase)) return m.Contains("manual") || m.Contains("thu cong");
        if (methodId.Equals("M-CONC-CRANE", StringComparison.OrdinalIgnoreCase)) return m.Contains("crane") || m.Contains("can cau");
        if (methodId.Equals("M-CONC-PUMP-STATIC", StringComparison.OrdinalIgnoreCase)) return m.Contains("pump") || m.Contains("bom");
        if (methodId.Equals("M-CONC-PUMP-MOBILE", StringComparison.OrdinalIgnoreCase)) return m.Contains("mobile") || m.Contains("tu hanh");
        return true;
    }

    private static bool CategoryCompatible(NormVariant variant, FactBag facts)
    {
        if (string.IsNullOrWhiteSpace(variant.CategoryId)) return true;
        if (!facts.TryGet("ElementType", out var val)) return true;
        var e = ValueTools.NormalizeText(val);
        return variant.CategoryId switch
        {
            "WC-CONC-COL" => e.Contains("column") || e.Contains("cot"),
            "WC-CONC-WALL" => e.Contains("wall") || e.Contains("tuong"),
            _ => true
        };
    }

    private static void NormalizeDerivedFacts(ElementFacts element, string workItemId, FactBag facts)
    {
        // Aliases that make the database rules consistent with common BIM naming.
        if (facts.TryGet("Thickness", out var thickness) && !facts.TryGet("WallThickness", out _)) facts.Set("WallThickness", thickness);
        if (facts.TryGet("WallThickness", out var wallThickness) && !facts.TryGet("Thickness", out _)) facts.Set("Thickness", wallThickness);

        if (!facts.TryGet("ElementType", out _))
        {
            var inferred = element.RevitCategory switch
            {
                "OST_StructuralColumns" => "Column",
                "OST_Walls" => "Wall",
                "OST_Floors" => "Slab",
                "OST_StructuralFraming" => "Beam",
                "OST_StructuralFoundation" => "Foundation",
                "OST_Roofs" => "Roof",
                "OST_Ceilings" => "Ceiling",
                "OST_Topography" => "Excavation",
                "OST_BuildingPad" => "Fill",
                "OST_Site" => "Site",
                "OST_Roads" => "Road",
                "OST_Conduit" => "Conduit",
                "OST_CableTray" => "Tray",
                "OST_LightingFixtures" => "Light",
                "OST_ElectricalFixtures" => "Switch",
                "OST_ElectricalEquipment" => "Protect",
                "OST_MechanicalEquipment" => "Equipment",
                "OST_DuctCurves" => "Duct",
                "OST_PipeCurves" => "Pipe",
                "OST_PipeFitting" => "Fitting",
                "OST_PipeAccessory" => "Valve",
                "OST_PlumbingFixtures" => "Fixture",
                "OST_PipeInsulations" => "Insulation",
                "OST_DuctInsulations" => "Insulation",
                "OST_FireAlarmDevices" => "FireAlarm",
                _ => ""
            };
            if (!string.IsNullOrWhiteSpace(inferred)) facts.Set("ElementType", inferred);
        }

        if (!facts.TryGet("Height", out _) && facts.TryGet("QS_ConstructionHeight", out var ch))
            facts.Set("Height", ch);

        if (workItemId == "XD-MASON-BRICK" && facts.TryGet("Thickness", out var t)) facts.Set("WallThickness", t);

        if (workItemId == "MEP-INSUL")
        {
            if (facts.TryGet("Thickness", out var insTh) && !facts.TryGet("InsulationThickness", out _))
                facts.Set("InsulationThickness", insTh);
            if (facts.TryGet("InsulationThickness", out var insTh2) && !facts.TryGet("Thickness", out _))
                facts.Set("Thickness", insTh2);
        }
    }

    private List<string> PrioritizeMissing(List<string> missing, string workItemId)
    {
        if (_data.SelectorSchemas.FirstOrDefault(x => x.WorkItemId.Equals(workItemId, StringComparison.OrdinalIgnoreCase)) is { } schema)
        {
            var order = ValueTools.SplitTokens(schema.SelectorKeys, '|');
            return missing.OrderBy(x =>
                          {
                              var i = Array.FindIndex(order, o => o.Equals(x, StringComparison.OrdinalIgnoreCase));
                              return i >= 0 ? i : 999;
                          })
                          .ThenBy(x => x).ToList();
        }
        return missing.OrderBy(x => x).ToList();
    }
}
