// Copyright (c) 2026 Jake Pine
// SPDX-License-Identifier: MIT
// This software is provided "as is", without warranty of any kind. Use at your own risk.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using UnityEngine;

/// <summary>
/// Propagates inspector attributes across multiple fields using Batch marker attributes in source.
/// Put real [Attributes] in a single [ ] together with a [BatchBegin("name")] marker; every field that follows
/// receives a clone of those attributes through the matching [BatchEnd("name")] (inclusive), or to the end of the
/// class body if no end is given. [BatchDefine("name")] stores a reusable set for [BatchApply("name")] without
/// propagating, and [BatchBegin] also defines its name so it can be applied elsewhere. If two batch markers share a
/// single [ ], BatchBegin always wins and the first one wins among duplicates. See OdinBatchAttribute
/// for every usage pattern.
/// Attribute instances are always taken from the compiled field that carries the [BatchBegin]/[BatchDefine] — source
/// is only used to discover the batch markers, their grouping inside one [ ], and field order. Constructor args,
/// nameof(), $"" interpolation, and Odin "$"/"@" resolvers are evaluated once by the compiler on the defining field;
/// propagated copies clone that compiled attribute object.
/// </summary>
public sealed class OdinBatchAttributeProcessor : OdinAttributeProcessor<object>
{
    /// <summary>
    /// Set to false to disable the entire batch-attribute processor. When false,
    /// ProcessChildMemberAttributes returns immediately — no source parsing, no cache
    /// population, and no BatchMarker removal. Zero overhead per repaint.
    /// </summary>
    public static readonly bool ENABLED = true;

    private static readonly Dictionary<Type, Dictionary<string, List<Attribute>>> memberAttributesCache =
        new Dictionary<Type, Dictionary<string, List<Attribute>>>();

    private static readonly Dictionary<string, Type> attributeTypeCache = new Dictionary<string, Type>(StringComparer.Ordinal);

    private enum BatchMarkerKind
    {
        Begin,
        End,
        Define,
        Apply
    }

    static OdinBatchAttributeProcessor()
    {
        UnityEditor.AssemblyReloadEvents.afterAssemblyReload += ClearCache;
    }

    public static void ClearCache()
    {
        memberAttributesCache.Clear();
        attributeTypeCache.Clear();
        OdinSourceFileHelper.ClearCache();
    }

    public override void ProcessChildMemberAttributes(InspectorProperty property, MemberInfo member, List<Attribute> attributes)
    {
        if (!ENABLED) return;

        if (member == null || member.DeclaringType == null)
        {
            return;
        }

        // Batch markers are processing directives only — never let them reach Odin's inspector pipeline.
        attributes.RemoveAll(IsBatchMarkerAttribute);

        List<Attribute> sourceAttributes = GetAttributesFromSource(member);
        if (sourceAttributes == null || sourceAttributes.Count == 0)
        {
            return;
        }

        for (int i = 0; i < sourceAttributes.Count; i++)
        {
            Attribute sourceAttribute = sourceAttributes[i];
            if (sourceAttribute == null)
            {
                continue;
            }

            bool alreadyPresent = false;
            Type sourceAttributeType = sourceAttribute.GetType();
            for (int j = 0; j < attributes.Count; j++)
            {
                if (attributes[j] != null && attributes[j].GetType() == sourceAttributeType)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (!alreadyPresent && IsAttributeCompatibleWithMember(sourceAttribute, member))
            {
                // Hand Odin a fresh clone every rebuild. Odin mutates group attributes during group
                // resolution (PropertyGroupAttribute.Combine modifies the instance), so sharing the cached
                // template instance would corrupt grouping (e.g. FoldoutGroup) on later inspector draws.
                Attribute attributeToAdd = OdinAttributeInstanceFactory.CreateFromCompiledTemplate(sourceAttribute) ?? sourceAttribute;
                attributes.Add(attributeToAdd);
            }
        }
    }

    private static bool IsAttributeCompatibleWithMember(Attribute attribute, MemberInfo member)
    {
        if (attribute == null || member == null)
        {
            return false;
        }

        // Odin attributes are declared with AttributeTargets.Property or .Method — not .Field —
        // because Odin processes them through its own inspector property system, not C# field targets.
        // Only apply the AttributeUsage field-compatibility check for non-Odin assemblies (e.g. Unity's
        // RangeAttribute, which genuinely must not be applied to non-numeric fields).
        string assemblyName = attribute.GetType().Assembly.GetName().Name ?? string.Empty;
        bool isOdinAttribute = assemblyName.StartsWith("Sirenix.", StringComparison.OrdinalIgnoreCase);

        if (!isOdinAttribute)
        {
            AttributeUsageAttribute usage = attribute.GetType().GetCustomAttribute<AttributeUsageAttribute>(true);
            if (usage != null)
            {
                if (member is FieldInfo && !usage.ValidOn.HasFlag(AttributeTargets.Field))
                {
                    return false;
                }

                if (member is PropertyInfo && !usage.ValidOn.HasFlag(AttributeTargets.Property))
                {
                    return false;
                }
            }
        }

        Type valueType = GetMemberValueType(member);
        if (attribute is RangeAttribute)
        {
            return valueType != null && IsNumericType(valueType);
        }

        return true;
    }

    private static Type GetMemberValueType(MemberInfo member)
    {
        if (member is FieldInfo fieldInfo)
        {
            return fieldInfo.FieldType;
        }

        if (member is PropertyInfo propertyInfo)
        {
            return propertyInfo.PropertyType;
        }

        return null;
    }

    private static bool IsNumericType(Type type)
    {
        if (type == null)
        {
            return false;
        }

        if (type.IsEnum)
        {
            return true;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Byte:
            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.UInt16:
            case TypeCode.Int32:
            case TypeCode.UInt32:
            case TypeCode.Int64:
            case TypeCode.UInt64:
            case TypeCode.Single:
            case TypeCode.Double:
            case TypeCode.Decimal:
                return true;
            default:
                return false;
        }
    }

    private static List<Attribute> GetAttributesFromSource(MemberInfo member)
    {
        Type declaringType = member.DeclaringType;

        if (!memberAttributesCache.TryGetValue(declaringType, out Dictionary<string, List<Attribute>> memberAttributes))
        {
            memberAttributes = ParseSourceDirectivesForType(declaringType);
            if (memberAttributes == null)
            {
                memberAttributes = new Dictionary<string, List<Attribute>>();
            }

            memberAttributesCache[declaringType] = memberAttributes;
        }

        if (memberAttributes == null)
        {
            return null;
        }

        if (memberAttributes.TryGetValue(member.Name, out List<Attribute> attributes))
        {
            return attributes;
        }

        return null;
    }

    private static Dictionary<string, List<Attribute>> ParseSourceDirectivesForType(Type type)
    {
        string[] lines = OdinSourceFileHelper.GetSourceLines(type);
        if (lines == null)
        {
            return null;
        }

        string typeKey = OdinSourceFileHelper.GetTypeKey(type);
        if (!OdinSourceFileHelper.TryGetTypeBodyRange(lines, typeKey, out int bodyStartIndex, out int bodyEndIndex))
        {
            return ParseSourceBatches(lines, type);
        }

        string[] scopedLines = new string[bodyEndIndex - bodyStartIndex + 1];
        Array.Copy(lines, bodyStartIndex, scopedLines, 0, scopedLines.Length);
        return ParseSourceBatches(scopedLines, type);
    }

    private static Dictionary<string, List<Attribute>> ParseSourceBatches(string[] lines, Type declaringType)
    {
        Dictionary<string, List<Attribute>> result = new Dictionary<string, List<Attribute>>();
        OdinBatchState state = new OdinBatchState();

        int i = 0;
        while (i < lines.Length)
        {
            if (TryCollectFieldBlock(lines, ref i, out List<string> attributeLines, out string fieldLine, out string memberName))
            {
                ProcessFieldBlock(declaringType, state, attributeLines, fieldLine, memberName, result);
                continue;
            }

            i++;
        }

        return result;
    }

    private static void ProcessFieldBlock(
        Type declaringType,
        OdinBatchState state,
        List<string> attributeLines,
        string fieldLine,
        string memberName,
        Dictionary<string, List<Attribute>> result)
    {
        List<List<string>> brackets = ParseFieldBrackets(attributeLines, fieldLine);

        // Resolve markers. BatchBegin wins over BatchDefine; the first of either wins; the [ ] that carries the
        // winning marker supplies the batch's attribute set (all the OTHER, non-marker entries in that [ ]).
        int beginBracket = -1;
        string beginName = null;
        int defineBracket = -1;
        string defineName = null;
        List<string> applyNames = null;
        List<string> endNames = null;
        bool endAll = false;

        for (int b = 0; b < brackets.Count; b++)
        {
            List<string> parts = brackets[b];
            for (int p = 0; p < parts.Count; p++)
            {
                if (!TryGetBatchMarker(parts[p], out BatchMarkerKind kind, out string markerName))
                {
                    continue;
                }

                switch (kind)
                {
                    case BatchMarkerKind.Begin:
                        if (beginBracket < 0)
                        {
                            beginBracket = b;
                            beginName = markerName;
                        }

                        break;
                    case BatchMarkerKind.Define:
                        if (defineBracket < 0)
                        {
                            defineBracket = b;
                            defineName = markerName;
                        }

                        break;
                    case BatchMarkerKind.Apply:
                        (applyNames ??= new List<string>()).Add(markerName);
                        break;
                    case BatchMarkerKind.End:
                        if (string.IsNullOrEmpty(markerName))
                        {
                            endAll = true;
                        }
                        else
                        {
                            (endNames ??= new List<string>()).Add(markerName);
                        }

                        break;
                }
            }
        }

        MemberInfo memberInfo = ResolveMember(declaringType, memberName);

        // BatchBegin: define + open. With no payload attributes, re-open a previously defined batch.
        if (beginBracket >= 0)
        {
            List<string> payload = GetNonMarkerTypeNames(brackets[beginBracket]);
            if (payload.Count > 0)
            {
                OdinAttributeBatchTemplate template = BuildTemplate(payload);
                EnrichTemplateFromMember(template, memberInfo);
                state.Open(beginName, template);
            }
            else
            {
                state.Reopen(beginName);
            }
        }
        else if (defineBracket >= 0)
        {
            // BatchDefine: store for later BatchApply, applied only to this field (which already has it compiled).
            List<string> payload = GetNonMarkerTypeNames(brackets[defineBracket]);
            if (payload.Count > 0)
            {
                OdinAttributeBatchTemplate template = BuildTemplate(payload);
                EnrichTemplateFromMember(template, memberInfo);
                state.Define(defineName, template);
            }
        }

        // Compute this field's attribute set: every open batch plus any applied definitions. Ends are processed
        // afterwards (inclusive end), so the field carrying [BatchEnd] still receives the batch.
        List<Attribute> fieldAttributes = new List<Attribute>();
        state.CollectOpenAttributes(fieldAttributes);

        if (applyNames != null)
        {
            for (int a = 0; a < applyNames.Count; a++)
            {
                if (state.TryGetDefined(applyNames[a], out OdinAttributeBatchTemplate applied))
                {
                    AddTemplateCompiled(fieldAttributes, applied);
                }
            }
        }

        if (fieldAttributes.Count > 0 && !string.IsNullOrEmpty(memberName))
        {
            if (!result.TryGetValue(memberName, out List<Attribute> existing))
            {
                existing = new List<Attribute>();
                result[memberName] = existing;
            }

            AppendUniqueAttributes(existing, fieldAttributes);
        }

        if (endAll)
        {
            state.EndAll();
        }

        if (endNames != null)
        {
            for (int e = 0; e < endNames.Count; e++)
            {
                state.End(endNames[e]);
            }
        }
    }

    // Collects every [ ] block carried by a field: its leading attribute lines first, then any attributes on the
    // field's own declaration line (e.g. [BatchEnd("b")] public int field3;). Each [ ] becomes one group, since
    // "all attributes within the same [ ] are part of the batch".
    private static List<List<string>> ParseFieldBrackets(List<string> attributeLines, string fieldLine)
    {
        List<List<string>> brackets = new List<List<string>>();

        if (attributeLines != null)
        {
            for (int i = 0; i < attributeLines.Count; i++)
            {
                AddBracketsFromLine(attributeLines[i], brackets);
            }
        }

        AddBracketsFromLine(fieldLine, brackets);

        return brackets;
    }

    private static void AddBracketsFromLine(string line, List<List<string>> brackets)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        OdinSourceFileHelper.SplitCodeAndComment(line, out string codePart, out _);
        List<string> bodies = ExtractAttributeBodies(codePart);
        for (int i = 0; i < bodies.Count; i++)
        {
            List<string> parts = SplitAttributeList(bodies[i]);
            List<string> cleaned = new List<string>(parts.Count);
            for (int p = 0; p < parts.Count; p++)
            {
                string part = parts[p].Trim();
                if (!string.IsNullOrEmpty(part))
                {
                    cleaned.Add(part);
                }
            }

            if (cleaned.Count > 0)
            {
                brackets.Add(cleaned);
            }
        }
    }

    private static List<string> GetNonMarkerTypeNames(List<string> bracketParts)
    {
        List<string> typeNames = new List<string>();
        for (int i = 0; i < bracketParts.Count; i++)
        {
            if (TryGetBatchMarker(bracketParts[i], out _, out _))
            {
                continue;
            }

            string typeName = ExtractAttributeTypeName(bracketParts[i]);
            if (!string.IsNullOrEmpty(typeName))
            {
                typeNames.Add(typeName);
            }
        }

        return typeNames;
    }

    private static string ExtractAttributeTypeName(string part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return null;
        }

        int openParen = part.IndexOf('(');
        string typeName = openParen >= 0 ? part.Substring(0, openParen).Trim() : part.Trim();

        int colonIndex = typeName.LastIndexOf(':');
        if (colonIndex >= 0)
        {
            typeName = typeName.Substring(colonIndex + 1).Trim();
        }

        int dotIndex = typeName.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            typeName = typeName.Substring(dotIndex + 1).Trim();
        }

        return typeName;
    }

    private static bool TryGetBatchMarker(string part, out BatchMarkerKind kind, out string name)
    {
        kind = default;
        name = string.Empty;

        string typeName = ExtractAttributeTypeName(part);
        if (string.IsNullOrEmpty(typeName))
        {
            return false;
        }

        if (typeName.EndsWith("Attribute", StringComparison.Ordinal))
        {
            typeName = typeName.Substring(0, typeName.Length - "Attribute".Length);
        }

        switch (typeName)
        {
            case "BatchBegin":
                kind = BatchMarkerKind.Begin;
                break;
            case "BatchEnd":
                kind = BatchMarkerKind.End;
                break;
            case "BatchDefine":
                kind = BatchMarkerKind.Define;
                break;
            case "BatchApply":
                kind = BatchMarkerKind.Apply;
                break;
            default:
                return false;
        }

        name = ParseMarkerName(part);
        return true;
    }

    // Reads the first string-literal argument from a marker, e.g. BatchBegin("b") -> "b", BatchBegin -> "".
    private static string ParseMarkerName(string part)
    {
        int open = part.IndexOf('(');
        if (open < 0)
        {
            return string.Empty;
        }

        int close = part.LastIndexOf(')');
        if (close <= open)
        {
            return string.Empty;
        }

        string args = part.Substring(open + 1, close - open - 1).Trim();
        if (args.Length == 0)
        {
            return string.Empty;
        }

        List<string> argParts = SplitAttributeList(args);
        if (argParts.Count == 0)
        {
            return string.Empty;
        }

        string first = argParts[0].Trim();
        if (first.Length >= 2 && first[0] == '"' && first[first.Length - 1] == '"')
        {
            return first.Substring(1, first.Length - 2);
        }

        return first;
    }

    private static OdinAttributeBatchTemplate BuildTemplate(List<string> attributeTypeNames)
    {
        OdinAttributeBatchTemplate template = new OdinAttributeBatchTemplate();
        for (int i = 0; i < attributeTypeNames.Count; i++)
        {
            string name = attributeTypeNames[i];
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            template.Entries.Add(new OdinAttributeBatchEntry
            {
                AttributeTypeName = name,
                AttributeType = ResolveAttributeType(name)
            });
        }

        return template;
    }

    // Captures the compiled attribute instance from the defining field for every entry in the template, matching
    // first by type identity, then by short name (robust against same-named attributes in different assemblies).
    private static void EnrichTemplateFromMember(OdinAttributeBatchTemplate template, MemberInfo member)
    {
        if (template == null || member == null)
        {
            return;
        }

        Attribute[] compiledAttributes = Attribute.GetCustomAttributes(member, inherit: true);
        for (int e = 0; e < template.Entries.Count; e++)
        {
            OdinAttributeBatchEntry entry = template.Entries[e];
            if (entry.CompiledTemplate != null)
            {
                continue;
            }

            if (entry.AttributeType == null && string.IsNullOrEmpty(entry.AttributeTypeName))
            {
                continue;
            }

            for (int i = 0; i < compiledAttributes.Length; i++)
            {
                Attribute compiledAttribute = compiledAttributes[i];
                if (compiledAttribute == null)
                {
                    continue;
                }

                bool isMatch = entry.AttributeType != null && entry.AttributeType.IsInstanceOfType(compiledAttribute);

                if (!isMatch && !string.IsNullOrEmpty(entry.AttributeTypeName))
                {
                    string compiledName = compiledAttribute.GetType().Name;
                    isMatch = string.Equals(compiledName, entry.AttributeTypeName, StringComparison.Ordinal)
                           || string.Equals(compiledName, entry.AttributeTypeName + "Attribute", StringComparison.Ordinal);
                }

                if (isMatch)
                {
                    entry.CompiledTemplate = compiledAttribute;
                    if (entry.AttributeType == null)
                    {
                        entry.AttributeType = compiledAttribute.GetType();
                    }

                    break;
                }
            }
        }
    }

    private static void AddTemplateCompiled(List<Attribute> destination, OdinAttributeBatchTemplate template)
    {
        if (template == null)
        {
            return;
        }

        for (int e = 0; e < template.Entries.Count; e++)
        {
            Attribute compiled = template.Entries[e].CompiledTemplate;
            if (compiled == null)
            {
                continue;
            }

            bool exists = false;
            Type compiledType = compiled.GetType();
            for (int j = 0; j < destination.Count; j++)
            {
                if (destination[j] != null && destination[j].GetType() == compiledType)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                destination.Add(compiled);
            }
        }
    }

    private static bool IsBatchMarkerAttribute(Attribute attribute)
    {
        return attribute is BatchBeginAttribute
            || attribute is BatchEndAttribute
            || attribute is BatchDefineAttribute
            || attribute is BatchApplyAttribute;
    }

    private static MemberInfo ResolveMember(Type declaringType, string memberName)
    {
        if (declaringType == null || string.IsNullOrEmpty(memberName))
        {
            return null;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Type currentType = declaringType;
        while (currentType != null)
        {
            FieldInfo fieldInfo = currentType.GetField(memberName, flags);
            if (fieldInfo != null)
            {
                return fieldInfo;
            }

            PropertyInfo propertyInfo = currentType.GetProperty(memberName, flags);
            if (propertyInfo != null)
            {
                return propertyInfo;
            }

            MethodInfo[] methods = currentType.GetMethods(flags);
            for (int m = 0; m < methods.Length; m++)
            {
                if (string.Equals(methods[m].Name, memberName, StringComparison.Ordinal))
                {
                    return methods[m];
                }
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Tracks batch state while walking a type's fields top to bottom. "Defined" batches persist for
    /// <c>[BatchApply]</c>; "open" batches propagate their attributes to every field until closed. Names are
    /// case-sensitive; the empty name "" is a valid batch.
    /// </summary>
    private sealed class OdinBatchState
    {
        private readonly Dictionary<string, OdinAttributeBatchTemplate> defined =
            new Dictionary<string, OdinAttributeBatchTemplate>(StringComparer.Ordinal);

        private readonly Dictionary<string, OdinAttributeBatchTemplate> open =
            new Dictionary<string, OdinAttributeBatchTemplate>(StringComparer.Ordinal);

        // Open batch names in the order they were opened (oldest first). The last entry is the innermost batch;
        // when two open batches carry the same attribute type, the innermost one wins.
        private readonly List<string> openOrder = new List<string>();

        // BatchBegin carrying payload attributes: (re)define the name and start propagating it.
        public void Open(string name, OdinAttributeBatchTemplate template)
        {
            if (name == null || template == null)
            {
                return;
            }

            defined[name] = template;
            TrackOpen(name);
            open[name] = template;
        }

        // Bare BatchBegin (no payload): re-open the existing definition, or start an empty batch if undefined.
        public void Reopen(string name)
        {
            if (name == null)
            {
                return;
            }

            TrackOpen(name);
            open[name] = defined.TryGetValue(name, out OdinAttributeBatchTemplate template)
                ? template
                : new OdinAttributeBatchTemplate();
        }

        // Record (or refresh) a name as the most recently opened batch so it counts as innermost.
        private void TrackOpen(string name)
        {
            openOrder.Remove(name);
            openOrder.Add(name);
        }

        // BatchDefine: store for later BatchApply without propagating.
        public void Define(string name, OdinAttributeBatchTemplate template)
        {
            if (name == null || template == null)
            {
                return;
            }

            defined[name] = template;
        }

        public bool TryGetDefined(string name, out OdinAttributeBatchTemplate template)
        {
            if (name == null)
            {
                template = null;
                return false;
            }

            return defined.TryGetValue(name, out template);
        }

        public void End(string name)
        {
            if (name == null)
            {
                return;
            }

            if (open.Remove(name))
            {
                openOrder.Remove(name);
            }
        }

        public void EndAll()
        {
            open.Clear();
            openOrder.Clear();
        }

        public void CollectOpenAttributes(List<Attribute> destination)
        {
            // Walk innermost (most recently opened) batch first. AppendUniqueAttributes keeps the first
            // attribute of each type, so the innermost batch overwrites outer batches that carry the same
            // attribute type (e.g. a nested FoldoutGroup replaces the outer one) instead of duplicating it.
            for (int i = openOrder.Count - 1; i >= 0; i--)
            {
                if (open.TryGetValue(openOrder[i], out OdinAttributeBatchTemplate template))
                {
                    AddTemplateCompiled(destination, template);
                }
            }
        }
    }

    private static bool TryCollectFieldBlock(string[] lines, ref int index, out List<string> attributeLines, out string fieldLine, out string memberName)
    {
        int blockStartIndex = index;
        attributeLines = new List<string>();
        fieldLine = null;
        memberName = null;

        while (index < lines.Length)
        {
            string trimmed = lines[index].TrimStart();

            if (trimmed.StartsWith("//", StringComparison.Ordinal) && !trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                string codePart = GetEffectiveDeclLine(lines, index);
                if (IsMemberDeclaration(codePart))
                {
                    fieldLine = lines[index];
                    memberName = OdinSourceFileHelper.ExtractMemberName(codePart);
                    index = OdinSourceFileHelper.FindMemberEndLine(lines, index) + 1;
                    return true;
                }

                attributeLines.Add(lines[index]);
                index++;
                continue;
            }

            string effective = GetEffectiveDeclLine(lines, index);
            if (IsMemberDeclaration(effective))
            {
                fieldLine = lines[index];
                memberName = OdinSourceFileHelper.ExtractMemberName(effective);
                index = OdinSourceFileHelper.FindMemberEndLine(lines, index) + 1;
                return true;
            }

            break;
        }

        if (attributeLines.Count == 0)
        {
            return false;
        }

        while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
        {
            index++;
        }

        if (index >= lines.Length)
        {
            index = blockStartIndex;
            attributeLines.Clear();
            return false;
        }

        int declarationIndex = index;
        string nextFieldLine = GetEffectiveDeclLine(lines, declarationIndex);
        if (!IsMemberDeclaration(nextFieldLine))
        {
            index = blockStartIndex;
            attributeLines.Clear();
            return false;
        }

        fieldLine = lines[declarationIndex];
        memberName = OdinSourceFileHelper.ExtractMemberName(nextFieldLine);
        index = OdinSourceFileHelper.FindMemberEndLine(lines, declarationIndex) + 1;
        return true;
    }

    // Returns the declaration line's code with its opening brace appended when that brace sits on the FOLLOWING
    // line. A multi-line property/method (e.g. "public int FullProp" then "{" on the next line) otherwise has no
    // '{', ';', '=', or '(' on its declaration line, so the name extraction and member-detection regexes would
    // miss it. Appending the peeked "{" makes the single-line detectors recognize it as a member.
    private static string GetEffectiveDeclLine(string[] lines, int index)
    {
        OdinSourceFileHelper.SplitCodeAndComment(lines[index], out string codePart, out _);
        string stripped = codePart.TrimEnd();
        if (stripped.Length == 0)
        {
            return codePart;
        }

        char last = stripped[stripped.Length - 1];
        if (last == '{' || last == ';' || last == '=' || last == '(' || last == ',' || last == ')'
            || stripped.IndexOf('(') >= 0 || stripped.IndexOf('=') >= 0)
        {
            // Already terminated or carries the structural chars the single-line detectors need.
            return codePart;
        }

        for (int j = index + 1; j < lines.Length; j++)
        {
            string nextTrimmed = lines[j].TrimStart();
            if (nextTrimmed.Length == 0)
            {
                continue;
            }

            // Only a brace on the next line turns this into a multi-line property/method declaration.
            if (nextTrimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return codePart + " {";
            }

            break;
        }

        return codePart;
    }

    // A field, property, or method declaration — every one of these can carry Odin inspector attributes, so all
    // are valid batch recipients.
    private static bool IsMemberDeclaration(string codeLine)
    {
        return OdinSourceFileHelper.IsFieldDeclarationLine(codeLine)
            || OdinSourceFileHelper.IsPropertyOrMethodDeclarationLine(codeLine);
    }

    private static void AppendUniqueAttributes(List<Attribute> destination, List<Attribute> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            Attribute candidate = source[i];
            if (candidate == null)
            {
                continue;
            }

            bool exists = false;
            Type candidateType = candidate.GetType();
            for (int j = 0; j < destination.Count; j++)
            {
                if (destination[j] != null && destination[j].GetType() == candidateType)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                destination.Add(candidate);
            }
        }
    }

    // Splits "FoldoutGroup("Stats"), GUIColor(0.6f, 1f, 0.7f), ReadOnly" by top-level commas. Commas inside
    // (), [], {} or string literals are ignored, so e.g. [FoldoutGroup("Stats, Combat")] stays a single part.
    private static List<string> SplitAttributeList(string body)
    {
        List<string> parts = new List<string>();
        int depth = 0;
        int start = 0;
        bool inString = false;
        char stringChar = '\0';
        for (int i = 0; i < body.Length; i++)
        {
            char c = body[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < body.Length)
                {
                    i++;
                    continue;
                }

                if (c == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                stringChar = c;
                continue;
            }

            if (c == '(' || c == '[' || c == '{')
            {
                depth++;
            }
            else if (c == ')' || c == ']' || c == '}')
            {
                depth--;
            }
            else if (c == ',' && depth == 0)
            {
                parts.Add(body.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < body.Length)
        {
            parts.Add(body.Substring(start));
        }

        return parts;
    }

    private static List<string> ExtractAttributeBodies(string text)
    {
        List<string> bodies = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return bodies;
        }

        int bracketDepth = 0;
        int bodyStart = -1;
        bool inString = false;
        char stringChar = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    continue;
                }

                if (c == stringChar)
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                inString = true;
                stringChar = c;
                continue;
            }

            if (c == '[')
            {
                bracketDepth++;
                if (bracketDepth == 1)
                {
                    bodyStart = i + 1;
                }

                continue;
            }

            if (c == ']')
            {
                if (bracketDepth == 1 && bodyStart >= 0)
                {
                    bodies.Add(text.Substring(bodyStart, i - bodyStart).Trim());
                    bodyStart = -1;
                }

                if (bracketDepth > 0)
                {
                    bracketDepth--;
                }
            }
        }

        return bodies;
    }

    private static Type ResolveAttributeType(string shortName)
    {
        if (string.IsNullOrWhiteSpace(shortName))
        {
            return null;
        }

        if (attributeTypeCache.TryGetValue(shortName, out Type cachedType))
        {
            return cachedType;
        }

        string[] candidates = shortName.EndsWith("Attribute", StringComparison.Ordinal)
            ? new[] { shortName }
            : new[] { shortName + "Attribute", shortName };

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int a = 0; a < assemblies.Length; a++)
        {
            Assembly assembly = assemblies[a];
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            if (types == null)
            {
                continue;
            }

            for (int c = 0; c < candidates.Length; c++)
            {
                string candidate = candidates[c];
                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null || !typeof(Attribute).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (string.Equals(type.Name, candidate, StringComparison.Ordinal))
                    {
                        attributeTypeCache[shortName] = type;
                        return type;
                    }
                }
            }
        }

        return null;
    }

    // ── Private data types ───────────────────────────────────────────────────────────────────────

    /// <summary>One attribute slot: the source type hint and the compiled instance from the defining field.</summary>
    private sealed class OdinAttributeBatchEntry
    {
        public Type AttributeType;
        public string AttributeTypeName;
        public Attribute CompiledTemplate;
    }

    /// <summary>All attribute slots that share a single [ ] with a Batch marker.</summary>
    private sealed class OdinAttributeBatchTemplate
    {
        public readonly List<OdinAttributeBatchEntry> Entries = new List<OdinAttributeBatchEntry>();
    }
}
#endif

