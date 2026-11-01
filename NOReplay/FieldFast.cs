using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace NOReplay
{
    /// <summary>
    /// Typed field and property access. FieldInfo.SetValue boxes every float
    /// and bool. The compiled delegates write the same bits without a heap object.
    /// If the expression fails to compile, the delegate falls back to reflection.
    /// </summary>
    static class FieldFast
    {
        static readonly Dictionary<FieldInfo, Action<object, float>> SetFloats = new Dictionary<FieldInfo, Action<object, float>>();
        static readonly Dictionary<FieldInfo, Func<object, float>> GetFloats = new Dictionary<FieldInfo, Func<object, float>>();
        static readonly Dictionary<FieldInfo, Action<object, bool>> SetBools = new Dictionary<FieldInfo, Action<object, bool>>();
        static readonly Dictionary<FieldInfo, Func<bool>> GetStaticBools = new Dictionary<FieldInfo, Func<bool>>();
        static readonly Dictionary<PropertyInfo, Action<object, float>> SetFloatProps = new Dictionary<PropertyInfo, Action<object, float>>();
        static readonly Dictionary<PropertyInfo, Action<object, bool>> SetBoolProps = new Dictionary<PropertyInfo, Action<object, bool>>();
        static readonly Dictionary<PropertyInfo, Action<object, int>> SetIntProps = new Dictionary<PropertyInfo, Action<object, int>>();
        static readonly Dictionary<PropertyInfo, Func<object, bool>> GetBoolProps = new Dictionary<PropertyInfo, Func<object, bool>>();
        static readonly Dictionary<MethodInfo, Action<object>> Calls = new Dictionary<MethodInfo, Action<object>>();
        static readonly Dictionary<MethodInfo, Action<object, float, float, float>> Calls3 = new Dictionary<MethodInfo, Action<object, float, float, float>>();

        public static void SetFloat(FieldInfo field, object target, float value)
        {
            FloatSetter(field)(target, value);
        }

        public static float GetFloat(FieldInfo field, object target)
        {
            return FloatGetter(field)(target);
        }

        public static void SetBool(FieldInfo field, object target, bool value)
        {
            BoolSetter(field)(target, value);
        }

        public static Action<object, float> FloatSetter(FieldInfo field)
        {
            if (SetFloats.TryGetValue(field, out var set)) return set;
            set = CompileSetFloat(field) ?? ((o, v) => field.SetValue(field.IsStatic ? null : o, v));
            SetFloats[field] = set;
            return set;
        }

        public static Func<object, float> FloatGetter(FieldInfo field)
        {
            if (GetFloats.TryGetValue(field, out var get)) return get;
            get = CompileGetFloat(field) ?? (o => field.GetValue(field.IsStatic ? null : o) is float f ? f : 0f);
            GetFloats[field] = get;
            return get;
        }

        public static Action<object, bool> BoolSetter(FieldInfo field)
        {
            if (SetBools.TryGetValue(field, out var set)) return set;
            set = CompileSetBool(field) ?? ((o, v) => field.SetValue(field.IsStatic ? null : o, v));
            SetBools[field] = set;
            return set;
        }

        public static Func<bool> StaticBoolGetter(FieldInfo field)
        {
            if (GetStaticBools.TryGetValue(field, out var get)) return get;
            get = CompileGetStaticBool(field) ?? (() => field.GetValue(null) is bool b && b);
            GetStaticBools[field] = get;
            return get;
        }

        public static Action<object, float>? FloatPropSetter(PropertyInfo? prop)
        {
            if (prop == null) return null;
            if (SetFloatProps.TryGetValue(prop, out var set)) return set;
            set = CompileSetProp<float>(prop) ?? ((o, v) => prop.SetValue(o, v));
            SetFloatProps[prop] = set;
            return set;
        }

        public static Action<object, bool>? BoolPropSetter(PropertyInfo? prop)
        {
            if (prop == null) return null;
            if (SetBoolProps.TryGetValue(prop, out var set)) return set;
            set = CompileSetProp<bool>(prop) ?? ((o, v) => prop.SetValue(o, v));
            SetBoolProps[prop] = set;
            return set;
        }

        public static Action<object, int>? IntPropSetter(PropertyInfo? prop)
        {
            if (prop == null) return null;
            if (SetIntProps.TryGetValue(prop, out var set)) return set;
            set = CompileSetProp<int>(prop) ?? ((o, v) => prop.SetValue(o, v));
            SetIntProps[prop] = set;
            return set;
        }

        public static Func<object, bool>? BoolPropGetter(PropertyInfo? prop)
        {
            if (prop == null) return null;
            if (GetBoolProps.TryGetValue(prop, out var get)) return get;
            get = CompileGetPropBool(prop) ?? (o => prop.GetValue(o) is bool b && b);
            GetBoolProps[prop] = get;
            return get;
        }

        public static Action<object>? Call(MethodInfo? method)
        {
            if (method == null) return null;
            if (Calls.TryGetValue(method, out var call)) return call;
            call = CompileCall(method) ?? (o => method.Invoke(o, null));
            Calls[method] = call;
            return call;
        }

        /// <summary>
        /// Instance call (float, float, float). Null when the signature does not match,
        /// so the caller keeps MethodInfo.Invoke.
        /// </summary>
        public static Action<object, float, float, float>? CallFloat3(MethodInfo? method)
        {
            if (method == null) return null;
            if (Calls3.TryGetValue(method, out var call)) return call;
            call = CompileCall3(method);
            if (call == null) return null;
            Calls3[method] = call;
            return call;
        }

        static Action<object, float>? CompileSetFloat(FieldInfo field)
        {
            if (field.FieldType != typeof(float) || field.IsStatic) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var val = Expression.Parameter(typeof(float), "v");
                var body = Expression.Assign(Expression.Field(Expression.Convert(obj, Decl(field)), field), val);
                return Expression.Lambda<Action<object, float>>(body, obj, val).Compile();
            }
            catch { return null; }
        }

        static Func<object, float>? CompileGetFloat(FieldInfo field)
        {
            if (field.FieldType != typeof(float) || field.IsStatic) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var body = Expression.Field(Expression.Convert(obj, Decl(field)), field);
                return Expression.Lambda<Func<object, float>>(body, obj).Compile();
            }
            catch { return null; }
        }

        static Action<object, bool>? CompileSetBool(FieldInfo field)
        {
            if (field.FieldType != typeof(bool) || field.IsStatic) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var val = Expression.Parameter(typeof(bool), "v");
                var body = Expression.Assign(Expression.Field(Expression.Convert(obj, Decl(field)), field), val);
                return Expression.Lambda<Action<object, bool>>(body, obj, val).Compile();
            }
            catch { return null; }
        }

        static Func<bool>? CompileGetStaticBool(FieldInfo field)
        {
            if (field.FieldType != typeof(bool) || !field.IsStatic) return null;
            try
            {
                var body = Expression.Field(null, field);
                return Expression.Lambda<Func<bool>>(body).Compile();
            }
            catch { return null; }
        }

        static Action<object, T>? CompileSetProp<T>(PropertyInfo prop)
        {
            var set = prop.GetSetMethod(true);
            if (set == null || prop.PropertyType != typeof(T)) return null;
            var decl = prop.DeclaringType;
            if (decl == null || decl.IsValueType) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var val = Expression.Parameter(typeof(T), "v");
                var body = Expression.Call(Expression.Convert(obj, decl), set, val);
                return Expression.Lambda<Action<object, T>>(body, obj, val).Compile();
            }
            catch { return null; }
        }

        static Func<object, bool>? CompileGetPropBool(PropertyInfo prop)
        {
            var get = prop.GetGetMethod(true);
            if (get == null || prop.PropertyType != typeof(bool)) return null;
            var decl = prop.DeclaringType;
            if (decl == null || decl.IsValueType) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var body = Expression.Call(Expression.Convert(obj, decl), get);
                return Expression.Lambda<Func<object, bool>>(body, obj).Compile();
            }
            catch { return null; }
        }

        static Action<object>? CompileCall(MethodInfo method)
        {
            if (method.IsStatic || method.GetParameters().Length != 0) return null;
            var decl = method.DeclaringType;
            if (decl == null || decl.IsValueType) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var body = Expression.Call(Expression.Convert(obj, decl), method);
                if (method.ReturnType == typeof(void))
                    return Expression.Lambda<Action<object>>(body, obj).Compile();
                var ignore = Expression.Block(body, Expression.Empty());
                return Expression.Lambda<Action<object>>(ignore, obj).Compile();
            }
            catch { return null; }
        }

        static Action<object, float, float, float>? CompileCall3(MethodInfo method)
        {
            if (method.IsStatic) return null;
            var ps = method.GetParameters();
            if (ps.Length != 3) return null;
            if (ps[0].ParameterType != typeof(float) || ps[1].ParameterType != typeof(float) || ps[2].ParameterType != typeof(float))
                return null;
            var decl = method.DeclaringType;
            if (decl == null || decl.IsValueType) return null;
            try
            {
                var obj = Expression.Parameter(typeof(object), "o");
                var a = Expression.Parameter(typeof(float), "a");
                var b = Expression.Parameter(typeof(float), "b");
                var c = Expression.Parameter(typeof(float), "c");
                var body = Expression.Call(Expression.Convert(obj, decl), method, a, b, c);
                if (method.ReturnType == typeof(void))
                    return Expression.Lambda<Action<object, float, float, float>>(body, obj, a, b, c).Compile();
                return null;
            }
            catch { return null; }
        }

        static Type Decl(FieldInfo field) => field.DeclaringType ?? typeof(object);
    }
}
