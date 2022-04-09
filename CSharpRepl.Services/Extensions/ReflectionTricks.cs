using System;
using System.Linq;
using System.Reflection;

namespace CSharpRepl.Services.Extensions;
public static class ReflectionTricks
{
    private static BindingFlags ALL = (BindingFlags)0xffff;
    public static T Steal<T>(Type t, object o, string member) =>
                (T)(t.GetFields(ALL).SingleOrDefault(fld => fld.Name == member)?.GetValue(o) ??
                t.GetProperties(ALL).SingleOrDefault(prop => prop.Name == member)?.GetValue(o) ??
                (t.GetMethods(ALL).Where(mth => mth.Name == member).Skip(1).Any() ?
                    (object)(t.GetMethods(ALL).Where(mth => mth.Name == member).ToArray()) : // Several overloads
                    t.GetMethods(ALL).SingleOrDefault(mth => mth.Name == member))); // Just a single overload (or null)
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

    public static MethodInfo Overload(this MethodInfo[] overloads, params Type[] types) =>
        overloads.SingleOrDefault(
            mi => mi.GetParameters().Length == types.Length &&
                    mi.GetParameters().Zip(types, (pi, expectedType) => pi.ParameterType.IsAssignableFrom(expectedType)).All(b => b));
}
