using System;
using System.Collections.Generic;

namespace WeiboReader {
// Shared application state derived from bridge events. Views read this; the
// MainWindow routes events here and to the active page. All access is on the
// UI thread after dispatch.
public class UserRow {
    public string Uid = "";
    public string Nickname = "";
    public long Unread;
    public bool UnreadUnavailable;
    public string UpdatedAt = "";
    public bool Favorite;
    public string ProfileStatus = "";
    public string ProfileMessage = "";
}
public class PostRow {
    public string PostId = "";
    public string Title = "";
    public string Author = "";
    public string AuthorUid = "";
    public long Unread;
    public bool UnreadUnavailable;
    public bool Favorite;
    public string UpdatedAt = "";
    public bool HasData;
}
public class FollowRow {
    public string Uid = "";
    public string PostId = "";
    public string TargetKind = "user";
    public string Nickname = "";
    public bool Enabled;
    public string Status = "";
    public string Mode = "";
    public long Unread;
    public long Interval;
    public long RoundLimit;
    public long DailyBudget;
    public long BudgetUsed;
    public string NextCheckAt = "";
    public string Reason = "";
}
public class ReportRow {
    public string Id = "";
    public string Name = "";
    public string Category = "";
    public string Mode = "";
    public string Uid = "";
    public string PostId = "";
    public string Account = "";
    public long Count;
    public string ModifiedText = "";
    public long Modified;
}
public class TaskCard {
    public string Id = "";
    public string Status = "";
    public string Kind = "";
    public string Message = "";
    public string Query = "";
    public bool Automatic;
}

public class AppState {
    public string Workspace = "";
    public string Version = "";
    public List<UserRow> Users = new List<UserRow>();
    public List<PostRow> Posts = new List<PostRow>();
    public List<FollowRow> Follows = new List<FollowRow>();
    public List<ReportRow> Reports = new List<ReportRow>();
    public List<TaskCard> Tasks = new List<TaskCard>();
    public string AccountText = "";
    public long Credits;
    public string CreditsText = "";
    public bool HomeBlocked;
    public string BootstrapId = "";
    public long DocumentEpoch = 1;
    public string PendingPairRequest = "";
    public bool BootstrapSent;
    public bool EngineStoppedShown;
    public Dictionary<string, string> Nicknames = new Dictionary<string, string>();

    public UserRow FindUser(string uid) {
        foreach (var user in Users) if (user.Uid == uid) return user;
        return null;
    }
    public PostRow FindPost(string postId) {
        foreach (var post in Posts) if (post.PostId == postId) return post;
        return null;
    }
    public FollowRow FindFollow(string uid, string postId, string targetKind) {
        foreach (var follow in Follows) {
            if (follow.TargetKind != targetKind) continue;
            if (targetKind == "post" ? follow.PostId == postId : follow.Uid == uid) return follow;
        }
        return null;
    }
    public string DisplayName(string uid, string fallback) {
        if (!string.IsNullOrEmpty(fallback)) return fallback;
        string nick;
        if (Nicknames.TryGetValue(uid, out nick) && !string.IsNullOrEmpty(nick)) return nick;
        return uid;
    }

    // Apply one engine event; returns true when the event changed home data.
    public bool Apply(Dictionary<string, object> e) {
        var type = Json.S(e, "type");
        switch (type) {
            case "library": {
                Workspace = Json.S(e, "workspace");
                Version = Json.S(e, "version");
                HomeBlocked = Json.B(e, "homeBlocked");
                BootstrapId = Json.S(e, "bootstrapId");
                Credits = Json.N(e, "credits");
                return true;
            }
            case "homeUsers": {
                var users = new List<UserRow>();
                foreach (var item in Json.L(e, "users")) {
                    var m = Json.M(item);
                    if (m == null) continue;
                    var row = new UserRow {
                        Uid = Json.S(m, "uid"),
                        Nickname = Json.S(m, "nickname"),
                        Unread = Json.N(m, "unread"),
                        UnreadUnavailable = Json.B(m, "unreadUnavailable"),
                        UpdatedAt = Json.S(m, "updatedAt"),
                        ProfileStatus = Json.S(m, "profileStatus"),
                        ProfileMessage = Json.S(m, "profileMessage"),
                    };
                    if (Json.N(m, "favorite") == 1 || Json.B(m, "favorite")) row.Favorite = true;
                    users.Add(row);
                    Nicknames[row.Uid] = row.Nickname;
                }
                Users = users;
                return true;
            }
            case "postsHome": {
                var posts = new List<PostRow>();
                foreach (var item in Json.L(e, "posts")) {
                    var m = Json.M(item);
                    if (m == null) continue;
                    var row = new PostRow {
                        PostId = Json.S(m, "postId"),
                        Title = Json.S(m, "title"),
                        Author = Json.S(m, "author"),
                        AuthorUid = Json.S(m, "authorUid"),
                        Unread = Json.N(m, "unread"),
                        UnreadUnavailable = Json.B(m, "unreadUnavailable"),
                        UpdatedAt = Json.S(m, "updatedAt"),
                        HasData = Json.B(m, "hasData"),
                    };
                    if (Json.N(m, "favorite") == 1 || Json.B(m, "favorite")) row.Favorite = true;
                    posts.Add(row);
                }
                Posts = posts;
                return true;
            }
            case "followState": {
                var follows = new List<FollowRow>();
                foreach (var item in Json.L(e, "entries")) {
                    var m = Json.M(item);
                    if (m == null) continue;
                    var row = new FollowRow {
                        Uid = Json.S(m, "uid"),
                        PostId = Json.S(m, "postId"),
                        TargetKind = string.IsNullOrEmpty(Json.S(m, "targetKind")) ? "user" : Json.S(m, "targetKind"),
                        Nickname = Json.S(m, "nickname"),
                        Enabled = Json.B(m, "enabled"),
                        Status = Json.S(m, "status"),
                        Mode = Json.S(m, "mode"),
                        Unread = Json.N(m, "unread"),
                        Interval = Json.N(m, "interval"),
                        RoundLimit = Json.N(m, "roundLimit"),
                        DailyBudget = Json.N(m, "dailyBudget"),
                        NextCheckAt = Json.S(m, "nextCheckAt"),
                        Reason = Json.S(m, "reason"),
                    };
                    var inbox = Json.M(m.ContainsKey("inbox") ? m["inbox"] : null);
                    if (inbox != null) row.Unread = Json.N(inbox, "unread");
                    follows.Add(row);
                }
                Follows = follows;
                return true;
            }
            case "libraryData": {
                var reports = new List<ReportRow>();
                foreach (var item in Json.L(e, "reports")) {
                    var m = Json.M(item);
                    if (m == null) continue;
                    reports.Add(new ReportRow {
                        Id = Json.S(m, "id"),
                        Name = Json.S(m, "name"),
                        Category = Json.S(m, "category"),
                        Mode = Json.S(m, "mode"),
                        Uid = Json.S(m, "uid"),
                        PostId = Json.S(m, "postId"),
                        Account = Json.S(m, "account"),
                        Count = Json.N(m, "count"),
                        ModifiedText = Json.S(m, "modified"),
                        Modified = Json.N(m, "modifiedMs"),
                    });
                }
                Reports = reports;
                return true;
            }
            case "credits": {
                if (e.ContainsKey("finalBalance") && e["finalBalance"] != null) Credits = Json.N(e, "finalBalance");
                return false;
            }
            case "account": {
                AccountText = Json.S(e, "text");
                return false;
            }
        }
        return false;
    }
}
}
