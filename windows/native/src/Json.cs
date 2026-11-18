using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections;
using System.Web.Script.Serialization;

namespace WeiboReader {
// Thin JSON helpers over the in-box JavaScriptSerializer. Bridge events are
// Dictionary<string,object> trees; these accessors keep view code terse and
// null-safe. C# 5 only (in-box compiler).
public class Json {
    static JavaScriptSerializer serializer;
    public static JavaScriptSerializer Serializer {
        get {
            if (serializer == null) {
                serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = int.MaxValue;
                serializer.RecursionLimit = 256;
            }
            return serializer;
        }
    }
    public static Dictionary<string, object> Parse(string text) {
        if (string.IsNullOrEmpty(text)) return null;
        try { return Serializer.Deserialize<Dictionary<string, object>>(text); }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }
    public static string Serialize(object value) {
        try { return Serializer.Serialize(value); }
        catch (InvalidOperationException) { return "{}"; }
    }
    public static string S(IDictionary<string, object> map, string key) {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return "";
        return Convert.ToString(map[key]);
    }
    public static long N(IDictionary<string, object> map, string key) {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return 0;
        try { return Convert.ToInt64(map[key]); } catch { return 0; }
    }
    public static bool B(IDictionary<string, object> map, string key) {
        if (map == null || !map.ContainsKey(key) || map[key] == null) return false;
        try { return Convert.ToBoolean(map[key]); } catch { return false; }
    }
    public static List<object> L(IDictionary<string, object> map, string key) {
        if (map == null || !map.ContainsKey(key)) return new List<object>();
        // JavaScriptSerializer materializes arrays as ArrayList (probe-verified).
        var list = map[key] as List<object>;
        if (list != null) return list;
        var array = map[key] as object[];
        if (array != null) return new List<object>(array);
        var arrayList = map[key] as ArrayList;
        if (arrayList != null) return new List<object>(arrayList.ToArray());
        return new List<object>();
    }
    public static Dictionary<string, object> M(object value) {
        // Nested objects materialize as Dictionary<string, object> concrete type.
        return value as Dictionary<string, object>;
    }
    // Build a request payload: chained helper on a dictionary.
    public static Dictionary<string, object> Obj() { return new Dictionary<string, object>(); }
    public static Dictionary<string, object> Put(Dictionary<string, object> map, string key, object value) {
        map[key] = value; return map;
    }
}
}
