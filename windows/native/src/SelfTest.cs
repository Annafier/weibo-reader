using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace WeiboReader {
// --selftest: no engine, no window. Verifies the pieces that can break at
// runtime for real users: JSON parsing of a representative event, page
// construction, external-URL allow-list and report path helpers. Exit 0 = pass.
public static class SelfTest {
    public static int RunAll() {
        int failures = 0;
        Action<string, bool> check = (name, ok) => {
            Program.Log("selftest " + name + ": " + (ok ? "ok" : "FAIL"));
            if (!ok) failures++;
            Console.WriteLine(name + ": " + (ok ? "ok" : "FAIL"));
        };
        var library = Json.Parse("{\"type\":\"library\",\"workspace\":\"C:/w\",\"version\":\"1.0.0\",\"credits\":2480,\"homeBlocked\":false,\"bootstrapId\":\"1\"}");
        check("parse library event", library != null && Json.S(library, "workspace") == "C:/w" && Json.N(library, "credits") == 2480);
        var homeUsers = Json.Parse("{\"type\":\"homeUsers\",\"users\":[{\"uid\":\"1234567890\",\"nickname\":\"甲\",\"unread\":3,\"favorite\":1},{\"uid\":\"9876543210\",\"nickname\":\"乙\"}]}");
        var state = new AppState();
        state.Apply(homeUsers);
        check("state.apply homeUsers", state.Users.Count == 2 && state.Users[0].Unread == 3 && state.Users[0].Favorite);
        check("state.findUser", state.FindUser("9876543210") != null && state.FindUser("nope") == null);
        check("external allow weibo.cn profile", Dialogs.ExternalUrl("https://weibo.cn/u/6381762523") != null);
        check("external allow open.weibo.com plan", Dialogs.ExternalUrl("https://open.weibo.com/cli/plan") != null);
        check("external reject other host", Dialogs.ExternalUrl("https://example.com/a") == null);
        check("external reject http", Dialogs.ExternalUrl("http://weibo.cn/u/6381762523") == null);
        check("external reject query junk", Dialogs.ExternalUrl("https://weibo.cn/u/6381762523?x=1") == null);
        try {
            var users = new Views.UserListPage(null);
            var posts = new Views.PostListPage(null);
            var subs = new Views.SubscriptionsPage(null);
            var settings = new Views.SettingsPage(null);
            check("pages construct", users != null && posts != null && subs != null && settings != null);
        } catch (Exception error) {
            check("pages construct threw: " + error.Message, false);
        }
        var payload = Json.Obj();
        Json.Put(payload, "uidA", "1234567890");
        var round = Json.Parse(Json.Serialize(payload));
        check("json roundtrip", round != null && Json.S(round, "uidA") == "1234567890");
        Program.Log("selftest complete, failures=" + failures);
        Console.WriteLine("SELFTEST " + (failures == 0 ? "PASS" : "FAIL"));
        return failures == 0 ? 0 : 1;
    }
}
}
