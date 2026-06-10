// Copyright (c) 2026 Jake Pine
// SPDX-License-Identifier: MIT
// This software is provided "as is", without warranty of any kind. Use at your own risk.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Creates attribute instances via Activator.CreateInstance with constructor args, falling back to a
/// field-overlaid clone of a compiled template when no constructor can express the template's values.
/// </summary>
public static class OdinAttributeInstanceFactory
{
    public static Attribute Create(Type attributeType, object[] constructorArgs)
    {
        if (attributeType == null || !typeof(Attribute).IsAssignableFrom(attributeType))
        {
            return null;
        }

        if (constructorArgs == null || constructorArgs.Length == 0)
        {
            return TryCreate(attributeType, Array.Empty<object>());
        }

        Attribute instance = TryCreate(attributeType, constructorArgs);
        if (instance != null)
        {
            return instance;
        }

        for (int count = constructorArgs.Length - 1; count >= 1; count--)
        {
            object[] subset = new object[count];
            Array.Copy(constructorArgs, subset, count);
            instance = TryCreate(attributeType, subset);
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    /// <summary>
    /// Produces a clone of a compiled attribute. The attribute is first built through its real constructor so
    /// types that rely on constructor initialization — notably <c>PropertyGroupAttribute</c>-derived group
    /// attributes (FoldoutGroup, BoxGroup, TabGroup, etc.) — are set up correctly. Every instance field from the
    /// template is then overlaid so values the chosen constructor could not express (e.g. <c>GUIColor.Color</c>
    /// built from r,g,b,a, or a Unity <c>Range</c>'s readonly min/max) are preserved exactly. Source markers only
    /// decide which batches are open; the values always come from the compiled attribute.
    /// A brand new instance is returned on every call so Odin (which mutates group attributes during group
    /// resolution via <c>PropertyGroupAttribute.Combine</c>) never receives a shared instance.
    /// </summary>
    public static Attribute CreateFromCompiledTemplate(Attribute compiledTemplate)
    {
        if (compiledTemplate == null)
        {
            return null;
        }

        Type attributeType = compiledTemplate.GetType();

        Attribute instance = CreateFromCompiledTemplateViaConstructor(compiledTemplate);

        if (instance == null)
        {
            try
            {
                instance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(attributeType) as Attribute;
            }
            catch
            {
                instance = null;
            }
        }

        if (instance == null)
        {
            return null;
        }

        CopyInstanceFields(attributeType, compiledTemplate, instance);
        return instance;
    }

    private static void CopyInstanceFields(Type type, object source, object destination)
    {
        Type current = type;
        while (current != null && current != typeof(object))
        {
            FieldInfo[] fields = current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.IsStatic)
                {
                    continue;
                }

                try
                {
                    field.SetValue(destination, field.GetValue(source));
                }
                catch
                {
                }
            }

            current = current.BaseType;
        }
    }

    private static Attribute CreateFromCompiledTemplateViaConstructor(Attribute compiledTemplate)
    {
        if (compiledTemplate == null)
        {
            return null;
        }

        Type attributeType = compiledTemplate.GetType();
        ConstructorInfo[] constructors = attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Array.Sort(constructors, (ConstructorInfo a, ConstructorInfo b) => a.GetParameters().Length.CompareTo(b.GetParameters().Length));

        for (int i = 0; i < constructors.Length; i++)
        {
            ConstructorInfo constructor = constructors[i];
            ParameterInfo[] parameters = constructor.GetParameters();
            object[] constructorValues = new object[parameters.Length];
            bool matches = true;

            for (int p = 0; p < parameters.Length; p++)
            {
                if (!TryReadTemplateMemberValue(compiledTemplate, parameters[p], out object value))
                {
                    matches = false;
                    break;
                }

                constructorValues[p] = value;
            }

            if (!matches)
            {
                continue;
            }

            Attribute instance = TryCreate(attributeType, constructorValues);
            if (instance != null)
            {
                return instance;
            }
        }

        return null;
    }

    private static Attribute TryCreate(Type attributeType, object[] constructorArgs)
    {
        try
        {
            object instance = constructorArgs != null && constructorArgs.Length > 0
                ? Activator.CreateInstance(attributeType, constructorArgs)
                : Activator.CreateInstance(attributeType);
            return instance as Attribute;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadTemplateMemberValue(Attribute compiledTemplate, ParameterInfo parameter, out object value)
    {
        value = null;
        if (compiledTemplate == null || parameter == null)
        {
            return false;
        }

        Type templateType = compiledTemplate.GetType();
        string[] names = GetTemplateMemberLookupNames(parameter.Name);

        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            PropertyInfo property = templateType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanRead && parameter.ParameterType.IsAssignableFrom(property.PropertyType))
            {
                value = property.GetValue(compiledTemplate, null);
                return true;
            }

            FieldInfo field = templateType.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null && parameter.ParameterType.IsAssignableFrom(field.FieldType))
            {
                value = field.GetValue(compiledTemplate);
                return true;
            }
        }

        if (parameter.HasDefaultValue)
        {
            value = parameter.DefaultValue;
            return true;
        }

        return false;
    }

    private static string[] GetTemplateMemberLookupNames(string parameterName)
    {
        if (string.Equals(parameterName, "groupName", StringComparison.OrdinalIgnoreCase))
        {
            return new string[] { "GroupName", "groupName" };
        }

        return new string[] { parameterName, parameterName + "Name" };
    }
}

#endif
