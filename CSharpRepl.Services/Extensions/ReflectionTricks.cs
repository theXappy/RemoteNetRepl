﻿using System;
using System.Linq;
using System.Reflection;

namespace CSharpRepl.Services.Extensions;
public static class ReflectionTricks
{
    private static BindingFlags ALL = BindingFlags.FlattenHierarchy | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    public static T Steal<T>(Type t, object o, string member)
    {
        var value = t.GetFields(ALL).SingleOrDefault(fld => fld.Name == member)?.GetValue(o);
        if (value != null) return (T)value;
        value = t.GetProperties(ALL).SingleOrDefault(prop => prop.Name == member)?.GetValue(o);
        if (value != null) return (T)value;
        if (t.GetMethods(ALL).Where(mth => mth.Name == member).Skip(1).Any())
            value = (object)(t.GetMethods(ALL).Where(mth => mth.Name == member).ToArray());
        else
            value = t.GetMethods(ALL).SingleOrDefault(mth => mth.Name == member);

        return value != null ? (T)value : default;
        // Just a single overload (or null)
    }

    public static T Steal<T>(this object o, string member) => Steal<T>(o.GetType(), o, member);
    public static T Steal<T>(this Type o, string member) => Steal<T>(o, null, member);
    public static T DeepSteal<T>(this object o, string pathToInnerMember)
    {
        if (pathToInnerMember.Contains("."))
        {
            string rest = pathToInnerMember.Substring(0, pathToInnerMember.LastIndexOf('.'));
            pathToInnerMember = pathToInnerMember.Substring(pathToInnerMember.LastIndexOf('.') + 1);
            o = DeepSteal<object>(o, rest);
        }
        return Steal<T>(o, pathToInnerMember);
    }

    public static void SetField<T>(Type t, object o, string member, T newValue) =>
        t.GetFields(ALL).SingleOrDefault(fld => fld.Name == member)?.SetValue(o, newValue);
    public static void SetProperty<T>(Type t, object o, string member, T newValue) =>
        t.GetProperties(ALL).SingleOrDefault(fld => fld.Name == member)?.SetValue(o, newValue);

    public static void SetMember<T>(Type t, object o, string member, T newValue)
    {
        SetField(t, o, member, newValue);
        SetProperty(t, o, member, newValue);
    }

    public static void SetMember<T>(this object o, string member, T newValue) =>
        SetMember(o.GetType(), o, member, newValue);

    public static void IndianaJones<T>(this object o, string member, Func<T, T> replacer) =>
        o.SetMember(member, replacer(o.Steal<T>(member)));

    public static MethodInfo Overload(this MethodInfo[] overloads, params Type[] types) =>
        overloads.SingleOrDefault(
            mi => mi.GetParameters().Length == types.Length &&
                    mi.GetParameters().Zip(types, (pi, expectedType) => pi.ParameterType.IsAssignableFrom(expectedType)).All(b => b));
}