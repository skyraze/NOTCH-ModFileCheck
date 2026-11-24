// Tests for ModCompare. Run: dotnet run -c Release   (exit code 0 = all passed)
using System;
using System.Collections.Generic;
using System.Linq;
using NOTCH;
using NOTCH.Net;
using NOTCH.Admin;
using NOTCH.Panel;
using NOTCH.Catalogue;
using NOTCH.Consent;
using NOTCH.Sync;
using System.IO;

int failed = 0, passed = 0;
void Check(string name, bool ok) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + name); } }

const string H1 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
const string H2 = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
ModEntry S(string guid, string ver, string hash = H1, ModKind kind = ModKind.Plugin) =>
    new ModEntry { Guid = guid, Name = guid, Version = ver, Sha256 = hash, Kind = kind };
WireMod C(string guid, string ver, string hash = H1, ModKind kind = ModKind.Plugin) =>
    new WireMod { Guid = guid, Name = guid, Version = ver, Sha256 = Hex.ToBytes(hash), Kind = (byte)kind };
List<Problem> Run(ModEntry[] s, WireMod[] c, bool unlisted = true) => ModCompare.Run(s, c, unlisted);
ProblemKind? Only(List<Problem> p) => p.Count == 1 ? p[0].Kind : (ProblemKind?)null;

Check("identical mod: no problems", Run(new[] { S("a", "1.0.0") }, new[] { C("a", "1.0.0") }).Count == 0);
Check("server has extra mods the client lacks: no problems", Run(new[] { S("a", "1.0.0"), S("b", "2.0.0") }, new[] { C("a", "1.0.0") }).Count == 0);
Check("unlisted reported", Only(Run(new[] { S("a", "1.0.0") }, new[] { C("x", "1.0.0") })) == ProblemKind.Unlisted);
Check("unlisted ignored when switched off", Run(new[] { S("a", "1.0.0") }, new[] { C("x", "1.0.0") }, unlisted: false).Count == 0);
Check("client older", Only(Run(new[] { S("a", "1.2.0") }, new[] { C("a", "1.1.0") })) == ProblemKind.VersionOlder);
Check("client newer", Only(Run(new[] { S("a", "1.2.0") }, new[] { C("a", "1.3.0") })) == ProblemKind.VersionNewer);
Check("1.10 is newer than 1.9 (numeric, not text)", Only(Run(new[] { S("a", "1.9") }, new[] { C("a", "1.10") })) == ProblemKind.VersionNewer);
Check("non-numeric versions that differ", Only(Run(new[] { S("a", "1.0.0-beta") }, new[] { C("a", "1.0.0-rc1") })) == ProblemKind.VersionDiffers);
Check("non-numeric versions that are equal: fine", Run(new[] { S("a", "1.0.0-beta") }, new[] { C("a", "1.0.0-beta") }).Count == 0);
Check("leading v is ignored", Run(new[] { S("a", "v1.0.0") }, new[] { C("a", "1.0.0") }).Count == 0);
Check("same version, different file", Only(Run(new[] { S("a", "1.0.0", H1) }, new[] { C("a", "1.0.0", H2) })) == ProblemKind.HashMismatch);
Check("hash compare ignores upper/lower case", Run(new[] { S("a", "1.0.0", H1.ToUpperInvariant()) }, new[] { C("a", "1.0.0", H1) }).Count == 0);
Check("no hash on the client: not a hash problem", Run(new[] { S("a", "1.0.0", H1) }, new[] { C("a", "1.0.0", "") }).Count == 0);
Check("no hash on the server: not a hash problem", Run(new[] { S("a", "1.0.0", "") }, new[] { C("a", "1.0.0", H2) }).Count == 0);
Check("different version reports version only, not hash too", Run(new[] { S("a", "1.2.0", H1) }, new[] { C("a", "1.1.0", H2) }).Count == 1);
ModEntry Labelled(string ver, string from) => new ModEntry { Guid = "a", Name = "a", Version = ver, Sha256 = H1, Kind = ModKind.Plugin, CompatibleFrom = from };
Check("label: server 1.16 'compatible from 1.15' accepts 1.15", Run(new[] { Labelled("1.16.0", "1.15") }, new[] { C("a", "1.15.0", H2) }).Count == 0);
Check("label: …and 1.15.3, anything between", Run(new[] { Labelled("1.16.0", "1.15") }, new[] { C("a", "1.15.3", H2) }).Count == 0);
Check("label: …but not 1.14", Only(Run(new[] { Labelled("1.16.0", "1.15") }, new[] { C("a", "1.14.9") })) == ProblemKind.VersionOlder);
Check("label: never accepts a version NEWER than the server's copy", Only(Run(new[] { Labelled("1.16.0", "1.15") }, new[] { C("a", "1.17.0") })) == ProblemKind.VersionNewer);
Check("label: the client's copy has no say (server 1.15 with no label refuses 1.16)", Only(Run(new[] { S("a", "1.15.0") }, new[] { C("a", "1.16.0") })) == ProblemKind.VersionNewer);
Check("label: the same version still has its file checked", Only(Run(new[] { Labelled("1.16.0", "1.15") }, new[] { C("a", "1.16.0", H2) })) == ProblemKind.HashMismatch);
Check("label: an unreadable label is ignored (exact match)", Only(Run(new[] { Labelled("1.16.0", "soon") }, new[] { C("a", "1.15.0") })) == ProblemKind.VersionOlder);
Check("guid match ignores upper/lower case", Run(new[] { S("Com.Foo.Bar", "1.0.0") }, new[] { C("com.foo.bar", "1.0.0") }).Count == 0);
Check("same id but different kind is a different mod", Only(Run(new[] { S("a", "1.0.0", H1, ModKind.Plugin) }, new[] { C("a", "1.0.0", H1, ModKind.Nobp) })) == ProblemKind.Unlisted);
Check("several problems at once", Run(new[] { S("a", "1.0.0"), S("b", "2.0.0", H1) }, new[] { C("a", "0.9.0"), C("b", "2.0.0", H2), C("x", "1.0.0") }).Count == 3);
Check("empty and null lists are safe", Run(new ModEntry[0], new WireMod[0]).Count == 0 && ModCompare.Run(null, null, true).Count == 0);
var d = Run(new[] { S("a", "1.2.0") }, new[] { C("a", "1.1.0") })[0].Describe();
Check("message names both versions and the fix", d.Contains("1.2.0") && d.Contains("1.1.0") && d.Contains("Update to v1.2.0"));
var h = Run(new[] { S("a", "1.0.0", H1) }, new[] { C("a", "1.0.0", H2) })[0].Describe();
Check("hash message shows 12 characters and an ellipsis for each side", h.Contains("aaaaaaaaaaaa…") && h.Contains("bbbbbbbbbbbb…") && !h.Contains(H1));

// --- PlayerNote: what the player reads ---
var three = Run(new[] { S("a", "1.0.0"), S("b", "2.0.0", H1) }, new[] { C("a", "0.9.0"), C("b", "2.0.0", H2), C("x", "1.0.0") });
Check("notice header counts the problems", PlayerNote.Header(3) == "3 mod problems on this server:");
Check("one problem is singular", PlayerNote.Header(1) == "1 mod problem on this server:");
string refusal = PlayerNote.Refusal(three);
Check("refusal: header plus one line per problem", refusal.Split('\n').Length == 4 && refusal.StartsWith("[NOTCH] Can't join server. 3 problems detected\n"));
Check("refusal: one problem is singular", PlayerNote.Refusal(three.Take(1).ToList()).StartsWith("[NOTCH] Can't join server. 1 problem detected\n"));
var mixedOrder = new List<Problem> {
    new Problem { Kind = ProblemKind.Unlisted, Name = "U", ClientVersion = "1.0.0", ServerVersion = "" },
    new Problem { Kind = ProblemKind.HashMismatch, Name = "H", ClientVersion = "1.0.0", ServerVersion = "1.0.0" },
    new Problem { Kind = ProblemKind.VersionOlder, Name = "V", ClientVersion = "1.0.0", ServerVersion = "1.1.0" },
    new Problem { Kind = ProblemKind.MissingRequired, Name = "R", ClientVersion = "", ServerVersion = "2.0.0" },
    new Problem { Kind = ProblemKind.Banned, Name = "B", ClientVersion = "1.0.0", ServerVersion = "" } };
string[] ordered = PlayerNote.Refusal(mixedOrder).Split('\n');
Check("refusal: most serious first (banned, required, version) and the other two only counted", ordered.Length == 4 && ordered[0] == "[NOTCH] Can't join server. 5 problems detected, showing 3" && ordered[1] == "B is not allowed here" && ordered[2] == "Missing R v2.0.0" && ordered[3].StartsWith("V: need v1.1.0"));
var many = Enumerable.Range(0, 9).Select(i => C("x" + i, "1.0.0")).ToArray();
string capped = PlayerNote.Refusal(Run(new ModEntry[0], many));
Check("long refusals never exceed 4 lines: header says 9, showing 3", capped.Split('\n').Length == 4 && capped.StartsWith("[NOTCH] Can't join server. 9 problems detected, showing 3\n"));
Check("no-NOTCH refusal says to install NOTCH", PlayerNote.NoNotchRefusal.Contains("Install NOTCH"));
Check("refusal lines fit the game's box, which does not wrap (80 characters)", refusal.Split('\n').Concat(capped.Split('\n')).Concat(PlayerNote.NoNotchRefusal.Split('\n')).All(l => l.Length <= 80));
Check("refusal: a missing required mod reads 'Missing <name> v<version>'", new Problem { Kind = ProblemKind.MissingRequired, Name = "Blueprinter", ServerVersion = "2.1.1", ClientVersion = "" }.Short() == "Missing Blueprinter v2.1.1");
Check("refusal: a very long mod name is cut so the line still fits", new Problem { Kind = ProblemKind.MissingRequired, Name = new string('x', 120), ServerVersion = "1.0.0", ClientVersion = "" }.Short().Length <= 80);

// --- A problem survives the trip server -> client unchanged ---
Check("problem text is identical after the wire round trip", three.All(p => Problem.FromWire(p.ToWire()).Describe() == p.Describe()));
Problem back = Problem.FromWire(three.First(p => p.Kind == ProblemKind.HashMismatch).ToWire());
Check("full hashes survive the wire round trip", back.ServerHash == H1 && back.ClientHash == H2);

// --- CheckReport: the player's notch-checks.yaml ---
var when = new DateTime(2026, 11, 13, 21, 4, 12);
string entry = CheckReport.Entry(when, "Sky's \"Milsim\" Server: EU", "RequireNotch", false, three, g => g == "a" ? @"D:\Games\Sky's Stuff\a.dll" : null);
Check("entry starts with the time", entry.StartsWith("- check: 2026-11-13 21:04:12\n"));
Check("server name is double-quoted with quotes escaped", entry.Contains("  server: \"Sky's \\\"Milsim\\\" Server: EU\"\n"));
Check("result counts the problems", entry.Contains("  result: joined with 3 problems\n"));
Check("versions are quoted (1.10 must not become a number)", entry.Contains("server_version: \"1.0.0\"") && entry.Contains("client_version: \"0.9.0\""));
Check("hash problems carry both full hashes", entry.Contains("server_sha256: " + H1) && entry.Contains("client_sha256: " + H2));
Check("paths are single-quoted, apostrophe doubled, backslashes untouched", entry.Contains(@"file: 'D:\Games\Sky''s Stuff\a.dll'"));
Check("unlisted mods have no fix line", !entry.Split(new[] { "    - mod: " }, StringSplitOptions.None).First(x => x.Contains("not on this server")).Contains("fix:"));
Check("refused result", CheckReport.Entry(when, "s", "RequireMatch", true, three.Take(1).ToList(), null).Contains("result: refused, 1 problem\n"));
Check("all match: empty problem list", CheckReport.Entry(when, "s", "WarnOnly", false, new List<Problem>(), null).Contains("result: joined, all mods match\n  problems: []"));
Check("a line break in a server name cannot start a fake entry", !CheckReport.Entry(when, "evil\n- check: 1999", "Off", false, new List<Problem>(), null).Contains("\n- check: 1999"));
string file = "";
for (int i = 1; i <= 12; i++) file = CheckReport.Merge(file, CheckReport.Entry(when.AddMinutes(i), "s" + i, "WarnOnly", false, new List<Problem>(), null));
int count = file.Split(new[] { "\n- check: " }, StringSplitOptions.None).Length - 1;
Check("the file keeps 10 checks", count == 10);
Check("newest first, oldest dropped", file.IndexOf("\"s12\"") < file.IndexOf("\"s11\"") && !file.Contains("\"s2\"") && file.Contains("\"s3\""));
Check("one header, at the top", file.StartsWith(CheckReport.Header) && file.Split(new[] { CheckReport.Header }, StringSplitOptions.None).Length == 2);
Check("Windows line endings in an existing file are handled", CheckReport.Merge(file.Replace("\n", "\r\n"), entry).Split(new[] { "\n- check: " }, StringSplitOptions.None).Length - 1 == 10);

// --- AdminList: admins.txt ---
const ulong A1 = 76561198000000001, A2 = 76561198000000002;
var ids = AdminList.Parse("# header\n\n76561198000000001  # Sky\r\n  76561198000000002\nnot-an-id\n12345\n", out var bad);
Check("reads IDs, with notes, spaces and Windows line endings", ids.SetEquals(new[] { A1, A2 }));
Check("reports lines that are not a Steam ID", bad.Count == 2 && bad.Contains("not-an-id") && bad.Contains("12345"));
Check("newer accounts past 7656119… are accepted", AdminList.TryParseSteamId("76561200000000123", out _) && AdminList.TryParseSteamId("76561202255233023", out _));
Check("the first and an early account are accepted", AdminList.TryParseSteamId("76561197960265728", out _) && AdminList.TryParseSteamId("76561197960265729", out _));
Check("a game-server ID, a group ID and out-of-range numbers are refused", !AdminList.TryParseSteamId("90297382495291395", out _) && !AdminList.TryParseSteamId("103582791429521412", out _) && !AdminList.TryParseSteamId("76561202255233024", out _) && !AdminList.TryParseSteamId("76561197960265727", out _));
Check("the template has no admins and no bad lines", AdminList.Parse(AdminList.Template, out var tb).Count == 0 && tb.Count == 0);
Check("empty and null text are safe", AdminList.Parse("", out _).Count == 0 && AdminList.Parse(null, out _).Count == 0);
string added = AdminList.Add(AdminList.Template, A1, "Sky#1\nfake");
Check("add: puts the ID on its own line with a cleaned note", added.EndsWith("76561198000000001  # Sky1 fake\n") && AdminList.Parse(added, out var ab).Contains(A1) && ab.Count == 0);
Check("add: keeps the existing text", added.StartsWith(AdminList.Template));
Check("add: an ID already listed is not added twice", AdminList.Add(added, A1, "again") == added);
Check("add: works on a file with no final line break", AdminList.Parse(AdminList.Add("76561198000000002", A1, ""), out _).SetEquals(new[] { A1, A2 }));
string removed = AdminList.Remove(AdminList.Add(added, A2, "Two"), A1);
Check("remove: takes out only that ID, keeps comments", AdminList.Parse(removed, out _).SetEquals(new[] { A2 }) && removed.Contains("# NOTCH admin list") && !removed.Contains("Sky1"));
Check("remove: an ID that is not listed changes nothing", AdminList.Remove(added, A2) == added);

var ents = AdminList.Entries("# x\n76561198000000001  # Sky\n76561198000000002\n76561198000000001 # twice\n");
Check("entries: IDs in file order with their notes, no duplicates", ents.Count == 2 && ents[0].Key == A1 && ents[0].Value == "Sky" && ents[1].Value == "");

// --- Per-mod rules: kick / warn / off ---
var ruleOff = new Dictionary<string, MismatchRule> { ["a"] = MismatchRule.Off };
Check("rule off: version and file are not compared for that mod only", ModCompare.Run(new[] { S("A", "1.2.0"), S("b", "1.2.0") }, new[] { C("a", "9.9.9", H2), C("b", "1.5.0") }, true, ruleOff).Single().Guid == "b");
var reqA = new ModEntry { Guid = "a", Name = "a", Version = "1.2.0", Required = true };
Check("rule off does not remove the need to HAVE a required mod", Only(ModCompare.Run(new[] { reqA }, new WireMod[0], true, ruleOff)) == ProblemKind.MissingRequired);
var withRule = ModCompare.Run(new[] { S("a", "1.2.0") }, new[] { C("a", "1.1.0") }, true, new Dictionary<string, MismatchRule> { ["a"] = MismatchRule.Kick });
Check("a problem carries the manager's rule", withRule.Single().Rule == MismatchRule.Kick);
var rulesIn = new Dictionary<string, MismatchRule> { ["com.b"] = MismatchRule.Off, ["weird \"id\" \\ x"] = MismatchRule.Warn, ["com.a"] = MismatchRule.Kick };
string ovText = RuleOverrides.Serialize(rulesIn, k => k == "com.a" ? "Mod A" : "");
var rulesOut = RuleOverrides.Parse(ovText, out var ovBad);
Check("rules survive save and load, including odd ids", ovBad.Count == 0 && rulesOut.Count == 3 && rulesIn.All(r => rulesOut[r.Key] == r.Value));
Check("the rules file is sorted, lower case, with the mod name as a note", ovText.Contains("\"com.a\": kick   # Mod A\n\"com.b\": off\n"));
var hand = RuleOverrides.Parse("# note\r\n\"Com.Foo\": WARN # x\r\nbroken line\n\"com.bar\": sometimes\n\"old\": major\n", out var handBad);
Check("hand edits: case and Windows line endings are fine; bad lines and the old words (major) are reported", hand.Count == 1 && hand["com.foo"] == MismatchRule.Warn && handBad.Count == 3);
Check("a number is not accepted as a rule", RuleOverrides.Parse("\"a\": 7\n", out var numBad).Count == 0 && numBad.Count == 1);

// --- What refuses the join: policy + required + the manager's rule ---
Problem Ver(bool required = false, MismatchRule? rule = null) => new Problem { Kind = ProblemKind.VersionOlder, Guid = "m", Name = "m", Required = required, Rule = rule };
Problem Of(ProblemKind k) => new Problem { Kind = k, Guid = "m", Name = "m", Required = k == ProblemKind.MissingRequired };
var all4 = new[] { JoinPolicy.Off, JoinPolicy.WarnOnly, JoinPolicy.RequireNotch, JoinPolicy.RequireMatch };
string Row(Problem p) => string.Join("", all4.Select(pol => ModCompare.Blocks(p, pol) ? "X" : "-"));     // Off, WarnOnly, RequireNotch, RequireMatch
Check("mismatch on an ordinary mod: only RequireMatch refuses", Row(Ver()) == "---X");
Check("mismatch on a required mod: RequireNotch refuses too", Row(Ver(required: true)) == "--XX");
Check("rule kick: refuses under RequireNotch even when not required — never under WarnOnly", Row(Ver(rule: MismatchRule.Kick)) == "--XX");
Check("rule warn: never refuses, even for a required mod under RequireMatch", Row(Ver(required: true, rule: MismatchRule.Warn)) == "----");
Check("a file mismatch follows the same rules as a version mismatch", ModCompare.Blocks(new Problem { Kind = ProblemKind.HashMismatch, Required = true }, JoinPolicy.RequireNotch) && !ModCompare.Blocks(new Problem { Kind = ProblemKind.HashMismatch }, JoinPolicy.RequireNotch));
Check("missing required mod: refuses under RequireNotch and RequireMatch", Row(Of(ProblemKind.MissingRequired)) == "--XX");
Check("banned mod: refuses under RequireNotch and RequireMatch", Row(Of(ProblemKind.Banned)) == "--XX");
Check("unlisted mod: only RequireMatch refuses", Row(Of(ProblemKind.Unlisted)) == "---X");

// --- Banned mods ---
var ban = new HashSet<string> { "cheat", "a" };
var banRun = ModCompare.Run(new[] { S("a", "1.0.0") }, new[] { C("Cheat", "3.0.0"), C("a", "1.0.0"), C("fine", "1.0.0") }, false, null, ban);
Check("banned: any version, known to the server or not, id compared without case", banRun.Count == 2 && banRun.All(p => p.Kind == ProblemKind.Banned));
Check("banned: the message says to remove it", banRun[0].Describe() == "Cheat: not allowed on this server. Remove it." && Problem.FromWire(banRun[0].ToWire()).Kind == ProblemKind.Banned);
string banText = BannedList.Serialize(new Dictionary<string, string> { ["com.x"] = "Some # Mod", ["nobp:jet"] = "" });
var banBack = BannedList.Parse(banText, out var banBad);
Check("banned.yaml: save and load with names", banBad.Count == 0 && banBack.Count == 2 && banBack["com.x"] == "Some  Mod" && banBack["nobp:jet"] == "");
Check("banned.yaml: only the word 'banned' is accepted", BannedList.Parse("\"a\": banned\n\"b\": yes\n", out var bb).Count == 1 && bb.Count == 1);
Check("the report names a banned mod", CheckReport.Entry(when, "s", "RequireNotch", true, banRun, null).Contains("issue: not allowed on this server\n      client_version: \"3.0.0\"\n      fix: Remove it\n"));

var named = new[] { new ModEntry { Guid = "g1", Name = "Configuration Manager" }, new ModEntry { Guid = "g2", Name = "FrontlineMap" }, new ModEntry { Guid = "g3", Name = "frontlinemap" }, new ModEntry { Guid = "g 4", Name = " " } };
var choices = ModChoices.Build(named);
Check("mod suggestions: spaces become underscores, none contain a space", choices.Any(c => c.Key == "Configuration_Manager") && choices.All(c => c.Key.Length > 0 && !c.Key.Any(char.IsWhiteSpace)));
Check("mod suggestions: same name twice gets a number, a blank name uses the id", choices.Select(c => c.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 4 && choices.Any(c => c.Key.EndsWith("_2")) && choices.Any(c => c.Key == "g_4"));

// --- Required mods (RequiresAll) ---
ModEntry Loaded(string guid, ModKind kind = ModKind.Plugin, string tag = "", string dep = null) =>
    new ModEntry { Guid = guid, Name = guid, Version = "1.0.0", Sha256 = H1, Kind = kind, Origin = ModOrigin.Loaded, ScopeTag = tag, Dependencies = dep == null ? new string[0] : new[] { dep } };
var set = new[]
{
    Loaded("plain"), Loaded("nobp:F-99", ModKind.Nobp), Loaded(RequiredRules.BlueprinterGuid), Loaded("usesbp", dep: RequiredRules.BlueprinterGuid),
    Loaded("tagged", tag: "requiresall"), Loaded("usesbp-clientonly", tag: "ClientOnly", dep: RequiredRules.BlueprinterGuid),
    new ModEntry { Guid = "nobp:folder", Name = "folder", Kind = ModKind.Nobp, Origin = ModOrigin.NoLoadFolder },
};
RequiredRules.Apply(set, null);
string req = string.Join(",", set.Where(e => e.Required).Select(e => e.Guid));
Check("required by default: Blueprinter content, Blueprinter, mods depending on it, and RequiresAll labels", req == "nobp:F-99," + RequiredRules.BlueprinterGuid + ",usesbp,tagged");
Check("every required mod carries a reason", set.Where(e => e.Required).All(e => e.RequiredWhy.Length > 0));
RequiredRules.Apply(set, new Dictionary<string, bool> { ["plain"] = true, ["nobp:f-99"] = false, ["nobp:folder"] = true });
Check("the manager's choice wins both ways, also for a mod the server only knows", set.First(e => e.Guid == "plain").Required && !set.First(e => e.Guid == "nobp:F-99").Required && set.First(e => e.Guid == "nobp:folder").Required);
RequiredRules.Apply(set, null);
var missing = ModCompare.Run(set, new[] { C("plain", "1.0.0"), C("usesbp", "1.0.0"), C("tagged", "1.0.0"), C(RequiredRules.BlueprinterGuid, "1.0.0") }, true);
Check("a required mod the player lacks is a problem", Only(missing) == ProblemKind.MissingRequired && missing[0].Guid == "nobp:F-99");
Check("its message says what to install", missing[0].Describe() == "nobp:F-99: required by this server, and you don't have it. Install nobp:F-99 v1.0.0.");
Check("a plugin with the same id is not the .nobp", ModCompare.Run(set, new[] { C("nobp:F-99", "1.0.0", H1, ModKind.Plugin) }, false).Count(p => p.Kind == ProblemKind.MissingRequired) == 4);
Check("a required mod in the wrong version is reported once, as a version problem", Only(ModCompare.Run(new[] { new ModEntry { Guid = "r", Name = "r", Version = "2.0.0", Required = true, Sha256 = H1 } }, new[] { C("r", "1.0.0") }, true)) == ProblemKind.VersionOlder);
Check("nothing is required unless marked", ModCompare.Run(new[] { S("a", "1.0.0"), S("b", "1.0.0") }, new WireMod[0], true).Count == 0);
Check("missing-required survives the wire and the report", Problem.FromWire(missing[0].ToWire()).Describe() == missing[0].Describe()
    && CheckReport.Entry(when, "s", "RequireMatch", true, missing, null).Contains("issue: missing required mod\n      required: yes\n      server_version: \"1.0.0\"\n      fix: Install nobp:F-99 v1.0.0\n"));
var reqFile = RequiredRules.Parse(RequiredRules.Serialize(new Dictionary<string, bool> { ["nobp:f-99"] = false, ["plain"] = true }, k => k), out var reqBad);
Check("required.yaml: save and load", reqBad.Count == 0 && reqFile.Count == 2 && reqFile["plain"] && !reqFile["nobp:f-99"]);
Check("required.yaml: hand edits accept on/off/true/false, reject other words", RequiredRules.Parse("\"a\": ON\n\"b\": false\n\"c\": maybe\n", out var rb).Count == 2 && rb.Count == 1);

// --- Which problems are about a required mod (these refuse the join under RequireNotch) ---
var mixed = ModCompare.Run(
    new[] { new ModEntry { Guid = "req", Name = "req", Version = "2.0.0", Sha256 = H1, Required = true }, new ModEntry { Guid = "reqhash", Name = "reqhash", Version = "1.0.0", Sha256 = H1, Required = true },
            new ModEntry { Guid = "gone", Name = "gone", Version = "1.0.0", Required = true }, S("opt", "2.0.0") },
    new[] { C("req", "1.0.0"), C("reqhash", "1.0.0", H2), C("opt", "1.0.0"), C("extra", "1.0.0") }, true);
Check("required: missing, wrong version and wrong file are flagged; optional and unlisted are not",
    string.Join(",", mixed.Where(p => p.Required).Select(p => p.Guid).OrderBy(g => g)) == "gone,req,reqhash" && mixed.Count == 5);
Check("the required flag survives the wire", mixed.All(p => Problem.FromWire(p.ToWire()).Required == p.Required));
string reqRefusal = PlayerNote.Refusal(mixed.Where(p => ModCompare.Blocks(p, JoinPolicy.RequireNotch)).ToList());
Check("RequireNotch refusal names only the required problems", reqRefusal.Split('\n').Length == 4 && reqRefusal.Contains("3 problems") && !reqRefusal.Contains("opt") && !reqRefusal.Contains("extra"));
Check("the report marks problems on required mods", CheckReport.Entry(when, "s", "RequireNotch", true, mixed, null).Split(new[] { "required: yes" }, StringSplitOptions.None).Length - 1 == 3);

// --- The menu's "Result now" column ---
string Res(JoinPolicy pol, byte where = PanelLogic.NoLoadFolder, bool required = false, byte rule = 0, bool banned = false, bool report = true) => PanelLogic.Result(pol, report, where, required, rule, banned);
Check("menu: an ordinary known mod under the default policy", Res(JoinPolicy.RequireNotch) == "mismatch: notice");
Check("menu: the same under RequireMatch", Res(JoinPolicy.RequireMatch) == "mismatch: refused");
Check("menu: a required mod", Res(JoinPolicy.RequireNotch, PanelLogic.OnServer, required: true) == "missing: refused · mismatch: refused");
Check("menu: a required mod under WarnOnly", Res(JoinPolicy.WarnOnly, PanelLogic.OnServer, required: true) == "missing: notice · mismatch: notice");
Check("menu: rule kick / warn / off", Res(JoinPolicy.RequireNotch, rule: 1) == "mismatch: refused" && Res(JoinPolicy.RequireMatch, rule: 2) == "mismatch: notice" && Res(JoinPolicy.RequireMatch, rule: 3) == "not compared");
Check("menu: required + rule off still needs the mod", Res(JoinPolicy.RequireNotch, PanelLogic.OnServer, required: true, rule: 3) == "missing: refused · not compared");
Check("menu: banned", Res(JoinPolicy.RequireNotch, banned: true) == "has it: refused" && Res(JoinPolicy.WarnOnly, banned: true) == "has it: notice");
Check("menu: a mod only seen on a player", Res(JoinPolicy.RequireNotch, PanelLogic.SeenOnPlayer) == "has it: notice" && Res(JoinPolicy.RequireMatch, PanelLogic.SeenOnPlayer) == "has it: refused" && Res(JoinPolicy.RequireMatch, PanelLogic.SeenOnPlayer, report: false).StartsWith("ignored"));
Check("menu: policy Off", Res(JoinPolicy.Off, banned: true).StartsWith("not checked"));
Check("required default: a plugin with a .nobp packed inside counts as Blueprinter content", RequiredRules.DefaultFor(new ModEntry { Guid = "aryx.pack", Kind = ModKind.Plugin, Origin = ModOrigin.Loaded, BlueprinterContent = true }, out var pw) && pw == "Blueprinter content");
Check("required default: an author's other label still wins over packed Blueprinter content", !RequiredRules.DefaultFor(new ModEntry { Guid = "aryx.pack", Kind = ModKind.Plugin, Origin = ModOrigin.Loaded, BlueprinterContent = true, ScopeTag = "ClientOnly" }, out _));
Check("required default: reason given, and none for a mod the server only knows", RequiredRules.DefaultFor(Loaded("nobp:x", ModKind.Nobp), out var dw) && dw == "Blueprinter content" && !RequiredRules.DefaultFor(new ModEntry { Guid = "nobp:y", Kind = ModKind.Nobp, Origin = ModOrigin.NoLoadFolder }, out _));

// --- Text arriving over the network ---
Check("clean: line breaks and control characters become spaces", SafeText.Clean("Evil\nMod\r\n[Info: NOTCH] fake line\t\0x") == "Evil Mod  [Info: NOTCH] fake line  x");
Check("clean: formatting tags are defused", SafeText.Clean("<size=500><color=red>BIG</color>") == "\u2039size=500\u203A\u2039color=red\u203ABIG\u2039/color\u203A");
Check("clean: text-reordering characters are dropped", SafeText.Clean("abc\u202Edef\u2066g") == "abcdefg");
Check("clean: long text is cut", SafeText.Clean(new string('x', 5000)).Length == SafeText.MaxLength);
Check("clean: ordinary names, versions and ids are untouched", SafeText.Clean("Configuration Manager 18.4.1 (com.bepis.bepinex.configurationmanager) – ünïcödé ✓") == "Configuration Manager 18.4.1 (com.bepis.bepinex.configurationmanager) – ünïcödé ✓");
Check("clean: null and empty are safe", SafeText.Clean(null) == "" && SafeText.Clean("") == "");

// ---- Stage 1a: the NOMNOM catalogue (gate, reader, checks, matching, schedule) ----
Check("gate: the two NOMNOM addresses are allowed", CatalogueGate.Allows(CatalogueGate.VersionUrl) && CatalogueGate.Allows(CatalogueGate.ManifestUrl));
foreach (string other in new[] { null, "", "https://example.com/manifest.json", "http://kopterbuzz.github.io/NOMNOM/manifest/manifest.json",
    "https://kopterbuzz.github.io/NOMNOM/manifest/manifest.json?x=1", "https://kopterbuzz.github.io/NOMNOM/manifest/manifest.json/",
    "https://KopterBuzz.github.io/NOMNOM/manifest/manifest.json", "https://kopterbuzz.github.io/NOMNOM/manifest/other.json",
    "https://kopterbuzz.github.io.evil.example/NOMNOM/manifest/manifest.json", " https://kopterbuzz.github.io/NOMNOM/manifest/version.json",
    "https://github.com/KopterBuzz/NOMNOM", "file:///C:/Windows/system.ini" })
    Check("gate: refuses " + (other ?? "(null)"), !CatalogueGate.Allows(other));
bool gateThrew = false; try { CatalogueGate.Require("https://example.com/"); } catch (InvalidOperationException) { gateThrew = true; }
Check("gate: Require throws for another address", gateThrew);
bool gateOk = true; try { CatalogueGate.Require(CatalogueGate.VersionUrl); } catch { gateOk = false; }
Check("gate: Require passes NOMNOM", gateOk);

const string Cat = @"[
 {""id"":""com.nikkorap.blueprinter"",""displayName"":""Blueprinter"",""artifacts"":[{""fileName"":""Blueprinter_2.1.1.dll"",""type"":""plugin"",""hash"":""sha256:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA""}]},
 {""id"":""aryx.mc260"",""artifacts"":[{""fileName"":""Aryx_MC260_Chimera_1.2.4.dll"",""type"":""addOn"",""extends"":{""id"":""com.nikkorap.blueprinter"",""version"":""1.8.17""}}]},
 {""id"":""p2082.expansion82"",""isClientOrServer"":""Both"",""artifacts"":[{""fileName"":""p2082.expansion82.dll"",""type"":""plugin"",""hash"":""sha256:" + H2 + @"""}]},
 {""id"":""HudThing"",""isClientOrServer"":""client"",""artifacts"":[{""fileName"":""HudThing.zip"",""type"":""plugin""},{""fileName"":""HudThing.zip"",""type"":""plugin""}]},
 {""id"":""HudSkin"",""artifacts"":[{""fileName"":""skin.nobp"",""type"":""addon"",""extends"":{""id"":""HudThing""}}]},
 {""id"":""ServerTool"",""isClientOrServer"":""Server"",""artifacts"":[{""fileName"":""ServerTool.dll""}]}
]";
CatalogueRead read = CatalogueReader.Read(new StringReader(Cat));
CatalogueEntry Ent(string id) => read.Entries.First(e => e.Id == id);
Check("reader: a catalogue is read", read.Ok && read.Entries.Count == 6);
Check("reader: client / server / both, any capitals", Ent("HudThing").Side == CatalogueSide.Client && Ent("ServerTool").Side == CatalogueSide.Server && Ent("p2082.expansion82").Side == CatalogueSide.Both);
Check("reader: no value stays None", Ent("com.nikkorap.blueprinter").Side == CatalogueSide.None);
Check("reader: 'addOn' counts as an add-on, with what it extends", Ent("aryx.mc260").Addon && Ent("aryx.mc260").Extends == "com.nikkorap.blueprinter");
Check("reader: a plugin is not an add-on", !Ent("p2082.expansion82").Addon);
Check("reader: hash loses its prefix and capitals", Ent("com.nikkorap.blueprinter").Hashes.Single() == H1);
Check("reader: a repeated file name is kept once", Ent("HudThing").FileNames.Count == 1);
Check("reader: not a list is refused", !CatalogueReader.Read(new StringReader("{\"id\":\"x\"}")).Ok);
Check("reader: an error page is refused", !CatalogueReader.Read(new StringReader("<html><body>404</body></html>")).Ok);
Check("reader: empty text is refused", !CatalogueReader.Read(new StringReader("")).Ok);
Check("reader: an empty list is refused", !CatalogueReader.Read(new StringReader("[]")).Ok);
Check("reader: a cut-off download is refused", !CatalogueReader.Read(new StringReader(Cat.Substring(0, Cat.Length / 2))).Ok);
Check("reader: a list of other things is refused", !CatalogueReader.Read(new StringReader("[1,2,3]")).Ok);
Check("reader: objects that are not catalogue entries are refused", !CatalogueReader.Read(new StringReader("[{\"name\":\"a\"},{\"name\":\"b\"}]")).Ok);
Check("reader: one odd entry among good ones is skipped", CatalogueReader.Read(new StringReader(Cat.Replace("[\n", "[\n {\"junk\":true},"))).Entries.Count == 6);
Check("reader: values of the wrong kind do not crash it", CatalogueReader.Read(new StringReader("[{\"id\":5,\"artifacts\":\"x\"},{\"id\":\"ok\",\"isClientOrServer\":7,\"artifacts\":[{\"fileName\":\"a.dll\",\"hash\":9,\"extends\":\"z\"}]}]")).Entries.Count == 1);

Check("limit: default is 8 MB", CatalogueCheck.DefaultCeiling == 8 * 1024 * 1024);
Check("limit: below 1 MB becomes 1 MB", CatalogueCheck.ClampCeiling(10) == CatalogueCheck.MinCeiling);
Check("limit: above 1 GB becomes 1 GB", CatalogueCheck.ClampCeiling(long.MaxValue) == CatalogueCheck.MaxCeiling);
Check("limit: a value in range is kept", CatalogueCheck.ClampCeiling(20 * CatalogueCheck.MB) == 20 * CatalogueCheck.MB);
Check("limit: warning starts at 64 MB", !CatalogueCheck.CeilingIsLarge(64 * CatalogueCheck.MB - 1) && CatalogueCheck.CeilingIsLarge(64 * CatalogueCheck.MB));

string[] ids10 = Enumerable.Range(0, 10).Select(i => "mod" + i).ToArray();
var mem = new CatalogueMemory();
Check("judge: a first download is accepted with nothing to compare", CatalogueCheck.Judge(900000, ids10, mem, out mem) == CatalogueVerdict.Accepted && mem.LastGoodSize == 900000 && mem.KnownIds.Count == 10);
Check("judge: the same again is accepted", CatalogueCheck.Judge(900500, ids10, mem, out mem) == CatalogueVerdict.Accepted);
Check("judge: growth is accepted (new mods added)", CatalogueCheck.Judge(1500000, ids10.Concat(new[] { "new1", "new2", "new3", "new4", "new5", "new6", "new7", "new8", "new9", "new10", "new11", "new12" }).ToArray(), mem, out mem) == CatalogueVerdict.Accepted);
var before = mem;
Check("judge: under a quarter of the last good size is too small", CatalogueCheck.Judge(100000, mem.KnownIds, mem, out mem) == CatalogueVerdict.TooSmall);
Check("judge: a rejection keeps the old memory", mem.LastGoodSize == before.LastGoodSize && mem.KnownIds.Count == before.KnownIds.Count && mem.Rejected.Length == 64);
mem = before;
string[] strangers = Enumerable.Range(0, 30).Select(i => "other" + i).ToArray();
Check("judge: fewer than half the known IDs is too different", CatalogueCheck.Judge(1400000, strangers, mem, out mem) == CatalogueVerdict.TooDifferent);
Check("judge: a different odd file next time is still rejected", CatalogueCheck.Judge(1400000, strangers.Concat(new[] { "x" }).ToArray(), mem, out mem) == CatalogueVerdict.TooDifferent);
Check("judge: the same result twice running is accepted (a real change)", CatalogueCheck.Judge(1400000, strangers.Concat(new[] { "x" }).ToArray(), mem, out mem) == CatalogueVerdict.Accepted && mem.Rejected == "" && mem.KnownIds.Count == 31);
Check("judge: exactly half the known IDs present is not too different", !CatalogueCheck.TooDifferent(new[] { "a", "b" }, new[] { "a", "b", "c", "d" }));
Check("judge: one fewer than half is", CatalogueCheck.TooDifferent(new[] { "a" }, new[] { "a", "b", "c", "d" }));
Check("judge: ID comparison ignores capitals", !CatalogueCheck.TooDifferent(new[] { "A", "B" }, new[] { "a", "b" }));
Check("fingerprint: order and capitals do not matter", CatalogueCheck.Fingerprint(new[] { "b", "A" }) == CatalogueCheck.Fingerprint(new[] { "a", "B" }));
Check("fingerprint: different lists differ", CatalogueCheck.Fingerprint(new[] { "a" }) != CatalogueCheck.Fingerprint(new[] { "a", "b" }));
var saved = CatalogueMemory.Parse(new CatalogueMemory { LastGoodSize = 898000, KnownIds = { "a.b", "C d" }, Rejected = "abc" }.Serialize());
Check("memory: survives being written and read", saved.LastGoodSize == 898000 && saved.KnownIds.SequenceEqual(new[] { "a.b", "C d" }) && saved.Rejected == "abc");
Check("memory: rubbish reads as empty", CatalogueMemory.Parse("hello\n::\nsize: -5\nsize: abc").First && CatalogueMemory.Parse(null).First);
Check("every verdict has wording", Enum.GetValues(typeof(CatalogueVerdict)).Cast<CatalogueVerdict>().All(v => CatalogueCheck.Reason(v).Length > 0));

ModEntry Inst(string guid, string path, string hash = "") => new ModEntry { Guid = guid, Name = guid, Version = "1.0.0", FilePath = path, Sha256 = hash, Kind = ModKind.Plugin };
CatalogueMatched matched = CatalogueMatch.Run(new[]
{
    Inst("com.nikkorap.blueprinter", @"D:\Game\BepInEx\plugins\Blueprinter\Blueprinter.dll"),                                   // by plugin ID
    Inst("AryxMC260Chimera", @"D:\Game\BepInEx\plugins\com.nikkorap.blueprinter\addons\aryx.mc260\Chimera.dll"),              // by folder name
    Inst("Blueprinter.shippack1", @"D:\Game\BepInEx\plugins\loose\renamed.dll", H2),                                          // by file hash
    Inst("some.server.tool", "/srv/game/BepInEx/plugins/ServerTool.dll"),                                                       // by file name
    Inst("unknown.mod", @"D:\Game\BepInEx\plugins\Unknown\Unknown.dll"),
}, read.Entries);
Check("match: by plugin ID", matched.ByGuid["com.nikkorap.blueprinter"].How == "plugin ID");
Check("match: by folder name", matched.ByGuid["AryxMC260Chimera"].Id == "aryx.mc260" && matched.ByGuid["AryxMC260Chimera"].How == "folder name");
Check("match: by file hash", matched.ByGuid["Blueprinter.shippack1"].Id == "p2082.expansion82" && matched.ByGuid["Blueprinter.shippack1"].Side == CatalogueSide.Both);
Check("match: by file name, forward slashes too", matched.ByGuid["some.server.tool"].Side == CatalogueSide.Server && matched.ByGuid["some.server.tool"].How == "file name");
Check("match: unmatched mods are listed", matched.Unmatched.Single() == "unknown.mod 1.0.0");
Check("match: an add-on with no value takes it from what it extends", CatalogueMatch.Run(new[] { Inst("skin", @"C:\g\BepInEx\plugins\HudSkin\s.dll") }, read.Entries).ByGuid["skin"].Side == CatalogueSide.Client);
Check("match: nothing to inherit stays None", matched.ByGuid["AryxMC260Chimera"].Side == CatalogueSide.None);
var twins = new[] { new CatalogueEntry { Id = "one", FileNames = { "plugin.dll" } }, new CatalogueEntry { Id = "two", FileNames = { "plugin.dll" } } };
Check("match: a file name two entries share matches neither", CatalogueMatch.Run(new[] { Inst("x", @"C:\g\BepInEx\plugins\plugin.dll") }, twins).Unmatched.Count == 1);
var loop = new[] { new CatalogueEntry { Id = "a", Extends = "b", FileNames = { "a.dll" } }, new CatalogueEntry { Id = "b", Extends = "a", FileNames = { "b.dll" } } };
Check("match: entries that extend each other do not hang", CatalogueMatch.Run(new[] { Inst("a", @"C:\g\BepInEx\plugins\a.dll") }, loop).ByGuid["a"].Side == CatalogueSide.None);
Check("match: BepInEx's own folders name no mod", CatalogueMatch.FolderOf(@"C:\g\BepInEx\plugins\x.dll") == "" && CatalogueMatch.FolderOf(@"C:\g\BepInEx\plugins\Mod\x.dll") == "Mod");
Check("match: nothing in, nothing out", CatalogueMatch.Run(null, null).ByGuid.Count == 0);

DateTime t0 = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
Check("schedule: never checked means check now", CatalogueSchedule.Due(t0, DateTime.MinValue, false));
Check("schedule: 59 minutes on, even a mission load does not check", !CatalogueSchedule.Due(t0.AddMinutes(59), t0, true));
Check("schedule: after an hour a mission load checks", CatalogueSchedule.Due(t0.AddMinutes(60), t0, true));
Check("schedule: after an hour with no mission load it waits", !CatalogueSchedule.Due(t0.AddMinutes(90), t0, false));
Check("schedule: after two hours it checks anyway", CatalogueSchedule.Due(t0.AddMinutes(120), t0, false));
Check("schedule: a clock set backwards checks", CatalogueSchedule.Due(t0.AddMinutes(-5), t0, false));

// ---- Stage 1a: the notice (answer, version, comparison) ----
ConsentNow now = ConsentState.Resolve(ConsentAnswer.NotAnswered, "0.13.0", "", "0.13.0");
Check("consent: a fresh install is not answered and not reset", now.Answer == ConsentAnswer.NotAnswered && !now.WasReset && now.Previous == "");
now = ConsentState.Resolve(ConsentAnswer.Accepted, "0.13.0", "", "0.13.0");
Check("consent: an answer for the current version stands", now.Answer == ConsentAnswer.Accepted && !now.WasReset);
now = ConsentState.Resolve(ConsentAnswer.Accepted, "v0.13.0", "", "0.13.0");
Check("consent: a leading v makes no difference", now.Answer == ConsentAnswer.Accepted && !now.WasReset);
now = ConsentState.Resolve(ConsentAnswer.Accepted, "0.13.0", "", "0.16.0");
Check("consent: a newer agreement resets a yes, and remembers the old version", now.Answer == ConsentAnswer.NotAnswered && now.WasReset && now.Version == "0.16.0" && now.Previous == "0.13.0");
now = ConsentState.Resolve(ConsentAnswer.Declined, "0.13.0", "", "0.16.0");
Check("consent: a newer agreement asks people who declined too", now.Answer == ConsentAnswer.NotAnswered && now.WasReset && now.Previous == "0.13.0");
now = ConsentState.Resolve(now.Answer, now.Version, now.Previous, "0.16.0");
Check("consent: after the reset the next start does not reset again", !now.WasReset && now.Answer == ConsentAnswer.NotAnswered && now.Previous == "0.13.0");
now = ConsentState.Resolve(ConsentAnswer.Accepted, "0.16.0", "0.13.0", "0.16.0");
Check("consent: answering by hand after a reset stands, and the old version is dropped", now.Answer == ConsentAnswer.Accepted && !now.WasReset && now.Previous == "");
now = ConsentState.Resolve(ConsentAnswer.NotAnswered, "0.13.0", "", "0.16.0");
Check("consent: never answered, then a new agreement: nothing to compare with", now.WasReset && now.Previous == "");
now = ConsentState.Resolve(ConsentAnswer.Accepted, "", "", "0.13.0");
Check("consent: a missing version is treated as another version", now.Answer == ConsentAnswer.NotAnswered && now.WasReset);
now = ConsentState.Resolve((ConsentAnswer)9, "0.13.0", "", "0.13.0");
Check("consent: an answer that is not one of the three is not answered", now.Answer == ConsentAnswer.NotAnswered);
Check("consent: only an explicit yes allows a connection", ConsentState.MayConnect(ConsentAnswer.Accepted) && !ConsentState.MayConnect(ConsentAnswer.NotAnswered) && !ConsentState.MayConnect(ConsentAnswer.Declined));

Check("agreement: the current version has its text", Agreement.CurrentText.Contains("NOTCH NETWORK ACCESS NOTICE AND CONSENT") && Agreement.TextOf(Agreement.Current) == Agreement.CurrentText);
Check("agreement: the text names its own version", Agreement.CurrentText.Contains("Agreement Version: " + Agreement.Current));
Check("agreement: the text says only NOMNOM is contacted and nothing is installed", Agreement.CurrentText.Contains("NOMNOM") && Agreement.CurrentText.Contains("does not download, install, modify"));
Check("agreement: no line is too wide for the window", Agreement.CurrentText.Split('\n').All(l => l.Length <= 80));
Check("agreement: an unknown version has no text", Agreement.TextOf("9.9.9") == null && Agreement.TextOf(null) == null);
Check("agreement: the first version has nothing to compare with", !Agreement.CanCompare(Agreement.Current) && !Agreement.CanCompare("") && !Agreement.CanCompare("0.1.0"));
List<DiffLine> diff = Agreement.Diff("one\ntwo\nthree\nfour", "one\nTWO\nthree\nfour\nfive");
Check("diff: unchanged lines stay, in order", string.Join("|", diff.Where(d => d.Kind == DiffKind.Same).Select(d => d.Text)) == "one|three|four");
Check("diff: a changed line shows as removed then added", string.Join("|", diff.Select(Agreement.Mark)) == "  one|- two|+ TWO|  three|  four|+ five");
Check("diff: identical texts have no changes", Agreement.Diff("a\nb", "a\r\nb\n").All(d => d.Kind == DiffKind.Same));
Check("diff: everything removed and everything added", Agreement.Diff("a\nb", "c").Count(d => d.Kind == DiffKind.Removed) == 2 && Agreement.Diff("a\nb", "c").Count(d => d.Kind == DiffKind.Added) == 1);
Check("diff: empty old text makes every line an addition", Agreement.Diff("", "x\ny").Count(d => d.Kind == DiffKind.Added) == 2);

// ---- Stage 1b: reading a download with a limit, and the catalogue's version file ----
byte[] kept = CatalogueCheck.ReadCapped(new MemoryStream(new byte[50000]), 50000, null, out CatalogueVerdict capVerdict);
Check("download: a file exactly at the limit is kept", kept != null && kept.Length == 50000 && capVerdict == CatalogueVerdict.Accepted);
Check("download: one byte over the limit is dropped", CatalogueCheck.ReadCapped(new MemoryStream(new byte[50001]), 50000, null, out capVerdict) == null && capVerdict == CatalogueVerdict.TooLarge);
Check("download: running out of time stops it", CatalogueCheck.ReadCapped(new MemoryStream(new byte[50000]), 50000, () => true, out capVerdict) == null && capVerdict == CatalogueVerdict.TookTooLong);
Check("download: an empty file reads as empty", CatalogueCheck.ReadCapped(new MemoryStream(), 100, null, out capVerdict).Length == 0);
Check("version: quotes and spaces are removed", CatalogueCheck.CleanVersion(" \"1.1.0.3179\"\n") == "1.1.0.3179");
Check("version: a bare version is kept", CatalogueCheck.CleanVersion("1.2.0-beta") == "1.2.0-beta");
Check("version: an error page is not a version", CatalogueCheck.CleanVersion("<html>404</html>") == "" && CatalogueCheck.CleanVersion("") == "" && CatalogueCheck.CleanVersion(null) == "");
Check("version: something far too long is not a version", CatalogueCheck.CleanVersion(new string('1', 41)) == "");

// ---- A mod unpacked into nested folders (found on Sky's game: Configuration Manager) ----
var nested = new[] { new CatalogueEntry { Id = "BepInEx.ConfigurationManager", FileNames = { "BepInEx.ConfigurationManager_v18.4.1.zip" } }, new CatalogueEntry { Id = "aryx.mc260", FileNames = { "a.dll" } }, new CatalogueEntry { Id = "com.nikkorap.blueprinter", FileNames = { "b.dll" } } };
Check("match: a folder further up names the mod", CatalogueMatch.Run(new[] { Inst("com.bepis.bepinex.configurationmanager", @"D:\Game\BepInEx\plugins\BepInEx.ConfigurationManager\BepInEx\plugins\ConfigurationManager\ConfigurationManager.dll") }, nested).ByGuid["com.bepis.bepinex.configurationmanager"].Id == "BepInEx.ConfigurationManager");
Check("match: the nearest folder wins over one further up", CatalogueMatch.Run(new[] { Inst("AryxMC260Chimera", @"D:\Game\BepInEx\plugins\com.nikkorap.blueprinter\addons\aryx.mc260\Chimera.dll") }, nested).ByGuid["AryxMC260Chimera"].Id == "aryx.mc260");
Check("match: folders above BepInEx are never used", CatalogueMatch.FoldersOf(@"D:\aryx.mc260\Game\BepInEx\plugins\Other\x.dll").SequenceEqual(new[] { "Other" }));
Check("match: a path without a BepInEx folder uses only the file's own folder", CatalogueMatch.FoldersOf(@"D:\a\b\c\x.dll").SequenceEqual(new[] { "c" }));

// ---- Stage 2a: packing settings for the network ----
SyncMod MakeMod(string guid, int settings, string value = "true") => new SyncMod { Guid = guid, Version = "1.0.0", Settings = Enumerable.Range(0, settings).Select(i => new SyncSetting { Section = "General", Key = "Setting" + i, Type = "System.Boolean", Value = value }).ToList() };
var twoMods = new[] { MakeMod("Blueprinter.shippack1", 15), MakeMod("com.nikkorap.blueprinter", 6) };
byte[] syncPacked = ConfigPack.Write(twoMods);
List<SyncMod> syncBack = ConfigPack.Read(syncPacked);
Check("pack: mods and settings survive the round trip", syncBack.Count == 2 && syncBack[0].Guid == "Blueprinter.shippack1" && syncBack[0].Settings.Count == 15 && syncBack[1].Settings[5].Key == "Setting5" && syncBack[1].Settings[5].Value == "true" && syncBack[0].Version == "1.0.0");
Check("pack: a typical server fits one packet", ConfigPack.Packets(syncPacked.Length) == 1);
Check("pack: non-English text survives", ConfigPack.Read(ConfigPack.Write(new[] { new SyncMod { Guid = "m", Settings = { new SyncSetting { Key = "k", Value = "Ünïcødé ✓" } } } }))[0].Settings[0].Value == "Ünïcødé ✓");
Check("pack: nothing packs and reads as nothing", ConfigPack.Read(ConfigPack.Write(null)).Count == 0);
foreach (var (badBlock, whatBlock) in new[] { (new byte[0], "empty"), (new byte[] { 1 }, "one byte"), (syncPacked.Take(syncPacked.Length / 2).ToArray(), "cut off"), (syncPacked.Concat(new byte[] { 0 }).ToArray(), "extra bytes"), (new byte[] { 255, 255, 0, 0 }, "claims 65535 mods") })
{
    bool refused = false; try { ConfigPack.Read(badBlock); } catch (InvalidDataException) { refused = true; }
    Check("pack: refuses a block that is " + whatBlock, refused);
}
bool longRefused = false; try { ConfigPack.Read(ConfigPack.Write(new[] { new SyncMod { Guid = "m", Settings = { new SyncSetting { Key = "k", Value = new string('a', ConfigPack.MaxText + 1) } } } })); } catch (InvalidDataException) { longRefused = true; }
Check("pack: refuses text over the limit", longRefused);
Check("packets: counted by 1180 bytes", ConfigPack.Packets(0) == 0 && ConfigPack.Packets(1) == 1 && ConfigPack.Packets(1180) == 1 && ConfigPack.Packets(1181) == 2);

byte[] smallSent = ConfigPack.Squeeze(syncPacked, out bool smallSqueezed);
Check("compress: one packet is sent as it is", !smallSqueezed && smallSent == syncPacked);
byte[] bigPlain = ConfigPack.Write(Enumerable.Range(0, 40).Select(i => MakeMod("mod.number." + i, 15)));
byte[] bigSent = ConfigPack.Squeeze(bigPlain, out bool bigSqueezed);
Check("compress: a large block is compressed when that saves packets", bigSqueezed && ConfigPack.Packets(bigSent.Length) < ConfigPack.Packets(bigPlain.Length));
Check("compress: it unpacks to the same bytes", ConfigPack.Unsqueeze(bigSent, true).SequenceEqual(bigPlain));
var syncNoise = new byte[3000]; new Random(7).NextBytes(syncNoise);
Check("compress: data that does not shrink is sent plain", ConfigPack.Squeeze(syncNoise, out bool noiseSqueezed) == syncNoise && !noiseSqueezed);
bool bombStopped = false; try { ConfigPack.Unsqueeze(ConfigPack.Squeeze(new byte[500000], out _), true); } catch (InvalidDataException) { bombStopped = true; }
Check("compress: a smallSent block that unpacks huge is stopped", bombStopped);
bool junkStopped = false; try { ConfigPack.Unsqueeze(syncNoise, true); } catch (InvalidDataException) { junkStopped = true; }
Check("compress: junk that is not compressed data is refused", junkStopped);

List<byte[]> onePart = ConfigPack.Parts(twoMods, out List<string> noneLeft);
Check("parts: a normal server is one part with nothing left out", onePart.Count == 1 && noneLeft.Count == 0);
var manyMods = Enumerable.Range(0, 10).Select(i => MakeMod("big.mod." + i, 400, "a value of some length")).ToList();   // about 23 KB each: two to a part
List<byte[]> manyParts = ConfigPack.Parts(manyMods, out List<string> manyLeft);
Check("parts: a large set is split, every part within the limit", manyParts.Count > 1 && manyParts.All(p => p.Length <= ConfigPack.SplitAt));
Check("parts: every mod arrives whole, in exactly one part", manyParts.SelectMany(p => ConfigPack.Read(p)).Select(m => m.Guid).SequenceEqual(manyMods.Select(m => m.Guid)) && manyLeft.Count == 0);
List<byte[]> withGiant = ConfigPack.Parts(new[] { MakeMod("giant", 5000, new string('x', 60)), MakeMod("normal", 3) }, out List<string> giantLeft);
Check("parts: a mod too big for one part is left out and named", giantLeft.SequenceEqual(new[] { "giant" }) && withGiant.Count == 1 && ConfigPack.Read(withGiant[0])[0].Guid == "normal");
List<byte[]> cappedParts = ConfigPack.Parts(Enumerable.Range(0, 200).Select(i => MakeMod("heavy." + i, 400, "a value of some length")), out List<string> cappedLeft);
Check("parts: beyond the overall ceiling the rest is left out and named", cappedParts.Count <= ConfigPack.MaxParts && cappedLeft.Count > 0 && cappedParts.SelectMany(p => ConfigPack.Read(p)).Count() + cappedLeft.Count == 200);

var partsBin = new PartsBin();
Check("arrival: nothing is handed over until the last part", partsBin.Add(0, manyParts.Count, manyParts[0]) == null);
List<SyncMod> arrivedMods = null;
for (int i = 1; i < manyParts.Count; i++) arrivedMods = partsBin.Add(i, manyParts.Count, manyParts[i]);
Check("arrival: all parts in gives every mod", arrivedMods != null && arrivedMods.Count == 10);
Check("arrival: a single part is complete at once", new PartsBin().Add(0, 1, syncPacked).Count == 2);
var restartBin = new PartsBin(); restartBin.Add(0, 2, manyParts[0]);
Check("arrival: a new first part starts over", restartBin.Add(0, 1, syncPacked).Count == 2);
Check("arrival: impossible part numbers are ignored", new PartsBin().Add(3, 2, syncPacked) == null && new PartsBin().Add(0, 99, syncPacked) == null && new PartsBin().Add(-1, 1, syncPacked) == null);
var dupBin = new PartsBin(); dupBin.Add(0, 2, manyParts[0]);
Check("arrival: the same part twice does not complete the set", dupBin.Add(0, 2, manyParts[0]) == null);

// ---- Stage 2a: whatBlock a server's value may do on a player's game ----
foreach (string okText in new[] { "", "Heartland", "P_Corvette1", "medium-range, long-range", "Ünïcødé 42", new string('a', 64) })
    Check("plain text: allowed '" + (okText.Length > 20 ? okText.Substring(0, 20) + "…" : okText) + "'", PlainText.Ok(okText));
foreach (string noText in new[] { null, "https://example.com", "www.example.com", @"C:\Windows\system32", "/etc/passwd", "..", "a.b", "user@host", "a:b", "100%", "a=b", "line\nbreak", "tab\there", "semi;colon", "<tag>", new string('a', 65) })
    Check("plain text: refused '" + (noText == null ? "(nothing)" : noText.Length > 20 ? noText.Substring(0, 20) + "…" : noText.Replace("\n", "\\n").Replace("\t", "\\t")) + "'", !PlainText.Ok(noText));
SyncSetting Sent(string type, string value) => new SyncSetting { Section = "s", Key = "k", Type = type, Value = value };
Check("apply: same setting, same type", ApplyRule.Decide(Sent("System.Boolean", "true"), "System.Boolean", false) == ApplyResult.Apply);
Check("apply: the player's mod has noText such setting", ApplyRule.Decide(Sent("System.Boolean", "true"), null, false) == ApplyResult.NoSuchSetting);
Check("apply: the type differs", ApplyRule.Decide(Sent("System.Int32", "5"), "System.Single", false) == ApplyResult.TypeDiffers);
Check("apply: marked NotchClient by its author", ApplyRule.Decide(Sent("System.Boolean", "true"), "System.Boolean", true) == ApplyResult.ClientOnly);
Check("apply: plain text is applied", ApplyRule.Decide(Sent("System.String", "P_Corvette1"), "System.String", false) == ApplyResult.Apply);
Check("apply: a link in a text setting is not applied", ApplyRule.Decide(Sent("System.String", "https://example.com/x"), "System.String", false) == ApplyResult.NotPlainText);
Check("apply: the text rule is for text settings only (a key binding passes)", ApplyRule.Decide(Sent("UnityEngine.KeyCode", "F + LeftControl"), "UnityEngine.KeyCode", false) == ApplyResult.Apply);
Check("apply: a type that only claims to be text does not skip the rule", ApplyRule.Decide(Sent("System.Int32", "https://example.com"), "System.String", false) == ApplyResult.TypeDiffers);
Check("apply: every outcome has wording", Enum.GetValues(typeof(ApplyResult)).Cast<ApplyResult>().All(r => ApplyRule.Why(r).Length > 0));

// ---- Stage 2c: config sync in the player's report file ----
string reportTwo = CheckReport.Header + "\n\n- check: 2026-11-23 10:00:00\n  server: \"A\"\n  policy: RequireNotch\n  result: joined, all mods match\n  problems: []\n\n- check: 2026-11-22 09:00:00\n  server: \"B\"\n  problems: []\n";
string withSettings = CheckReport.AddSettings(reportTwo, "Config sync: this server set 2 setting(s).", new[] { "Probe: [Probe] Text = bravo (yours: alpha)", "Probe: [Probe] Path not applied" });
Check("report: settings go into the newest check, before the older one", withSettings.IndexOf("  settings: \"Config sync: this server set 2 setting(s).\"") > withSettings.IndexOf("10:00:00") && withSettings.IndexOf("  settings:") < withSettings.IndexOf("09:00:00"));
Check("report: each detail is a quoted line", withSettings.Contains("    - \"Probe: [Probe] Text = bravo (yours: alpha)\"") && withSettings.Contains("    - \"Probe: [Probe] Path not applied\""));
Check("report: the older check is untouched and the blank line between checks stays", withSettings.Contains("  server: \"B\"\n  problems: []") && withSettings.Contains("\n\n- check: 2026-11-22 09:00:00"));
Check("report: a second sync on the same check replaces the first", CheckReport.AddSettings(withSettings, "second", new string[0]).Split('\n').Count(l => l.StartsWith("  settings")) == 1 && !CheckReport.AddSettings(withSettings, "second", new string[0]).Contains("settings_detail"));
Check("report: the next join's merge keeps the settings of the earlier check", CheckReport.Merge(withSettings, "- check: 2026-11-23 11:00:00\n  problems: []\n").Contains("    - \"Probe: [Probe] Text = bravo (yours: alpha)\""));
Check("report: a file with no check is left alone", CheckReport.AddSettings("# only a header\n", "x", null) == "# only a header\n" && CheckReport.AddSettings(null, "x", null) == "");
Check("report: text that could break the file is quoted", CheckReport.AddSettings(reportTwo, "a \"quoted\"\nline", null).Contains("  settings: \"a \\\"quoted\\\" line\""));

Check("report: a check with problems keeps them when settings are added", CheckReport.AddSettings(CheckReport.Header + "\n\n- check: 2026-11-23 10:00:00\n  problems:\n    - mod: \"X (x)\"\n      issue: missing\n", "s", new[] { "d" }).Contains("    - mod: \"X (x)\"\n      issue: missing\n  settings: \"s\"\n  settings_detail:\n    - \"d\""));
// ---- Stage 3a: which mods' settings sync, and the manager's per-mod choice ----
SyncVerdict Verdict(bool never, CatalogueSide side, bool required, SyncChoice choice) => SyncDecision.Decide(never, SyncLabel.None, "", side, required, choice);

// ---- Stage 4a/4b: the author's label and a mod's own sync ----
SyncVerdict ByLabel(SyncLabel label, string ownSync, CatalogueSide side, bool required, SyncChoice choice) => SyncDecision.Decide(false, label, ownSync, side, required, choice);
Check("label: the three words are read without regard to capitals, anything else is no label", SyncLabels.Parse("self") == SyncLabel.Self && SyncLabels.Parse(" NOTCH ") == SyncLabel.Notch && SyncLabels.Parse("Off") == SyncLabel.Off && SyncLabels.Parse("yes") == SyncLabel.None && SyncLabels.Parse(null) == SyncLabel.None);
Check("label: Self is off and locked, even for a required mod the manager set to sync", ByLabel(SyncLabel.Self, "", CatalogueSide.None, true, SyncChoice.Sync).Mode == SyncMode.Off && ByLabel(SyncLabel.Self, "", CatalogueSide.None, true, SyncChoice.Sync).Locked);
Check("label: Off is off and locked", ByLabel(SyncLabel.Off, "", CatalogueSide.Both, true, SyncChoice.Sync).Mode == SyncMode.Off && ByLabel(SyncLabel.Off, "", CatalogueSide.Both, true, SyncChoice.Auto).Locked);
Check("label: Notch syncs a mod that is not required", ByLabel(SyncLabel.Notch, "", CatalogueSide.None, false, SyncChoice.Auto).Mode == SyncMode.Sync && !ByLabel(SyncLabel.Notch, "", CatalogueSide.None, false, SyncChoice.Auto).Locked);
Check("label: Notch outranks the catalogue's client-only mark", ByLabel(SyncLabel.Notch, "", CatalogueSide.Client, false, SyncChoice.Auto).Mode == SyncMode.Sync);
Check("label: Notch outranks sync code found in the mod", ByLabel(SyncLabel.Notch, "ServerSync", CatalogueSide.None, true, SyncChoice.Auto).Mode == SyncMode.Sync);
Check("label: the manager can turn a Notch-labelled mod to warn or off", ByLabel(SyncLabel.Notch, "", CatalogueSide.None, true, SyncChoice.Warn).Mode == SyncMode.Warn && ByLabel(SyncLabel.Notch, "", CatalogueSide.None, true, SyncChoice.Off).Mode == SyncMode.Off);
Check("label: the never-send list still wins over Notch", SyncDecision.Decide(true, SyncLabel.Notch, "", CatalogueSide.None, true, SyncChoice.Sync).Mode == SyncMode.Off);
Check("own sync: with no label it is off and locked, and says which library", ByLabel(SyncLabel.None, "ServerSync", CatalogueSide.None, true, SyncChoice.Sync).Mode == SyncMode.Off && ByLabel(SyncLabel.None, "ServerSync", CatalogueSide.None, true, SyncChoice.Sync).Locked && ByLabel(SyncLabel.None, "ServerSync", CatalogueSide.None, true, SyncChoice.Auto).Why.Contains("ServerSync"));
Check("own sync: found as a referenced file", SyncLabels.OwnSync(new[] { "UnityEngine", "Jotunn", "BepInEx" }) == "Jotunn" && SyncLabels.OwnSync(new[] { "com.sigurd.csync" }) == "CSync");
Check("own sync: found as a namespace packed inside the mod", SyncLabels.OwnSync(new[] { "MyMod", "ServerSync", "" }) == "ServerSync" && SyncLabels.OwnSync(new[] { "CSync.Lib" }) == "CSync");
Check("own sync: a name that only contains the word is not a match", SyncLabels.OwnSync(new[] { "MyServerSyncHelper", "NotJotunn.Tools", "Sync" }) == "" && SyncLabels.OwnSync(null) == "" && SyncLabels.OwnSync(new string[] { null, "" }) == "");
Check("label and own sync: every reason is short enough for the table", new[] { ByLabel(SyncLabel.Self, "", CatalogueSide.None, true, SyncChoice.Auto), ByLabel(SyncLabel.Off, "", CatalogueSide.None, true, SyncChoice.Auto), ByLabel(SyncLabel.Notch, "", CatalogueSide.None, true, SyncChoice.Auto), ByLabel(SyncLabel.None, "ServerSync", CatalogueSide.None, true, SyncChoice.Auto) }.All(v => v.Why.Length > 0 && v.Why.Length <= 40));
Check("decide: a required mod syncs by default", Verdict(false, CatalogueSide.None, true, SyncChoice.Auto).Mode == SyncMode.Sync && !Verdict(false, CatalogueSide.None, true, SyncChoice.Auto).Locked);
Check("decide: a mod that is not required is not sent by default", Verdict(false, CatalogueSide.None, false, SyncChoice.Auto).Mode == SyncMode.Off);
Check("decide: 'Both' in the catalogue changes nothing", Verdict(false, CatalogueSide.Both, true, SyncChoice.Auto).Mode == SyncMode.Sync && Verdict(false, CatalogueSide.Both, false, SyncChoice.Auto).Mode == SyncMode.Off);
Check("decide: the manager can turn a required mod to warn or off", Verdict(false, CatalogueSide.None, true, SyncChoice.Warn).Mode == SyncMode.Warn && Verdict(false, CatalogueSide.None, true, SyncChoice.Off).Mode == SyncMode.Off);
Check("decide: the manager can sync a mod that is not required", Verdict(false, CatalogueSide.Both, false, SyncChoice.Sync).Mode == SyncMode.Sync);
Check("decide: a client-only mod is locked off, whatever the manager chose", Verdict(false, CatalogueSide.Client, true, SyncChoice.Sync).Mode == SyncMode.Off && Verdict(false, CatalogueSide.Client, true, SyncChoice.Sync).Locked);
Check("decide: a server-only mod is locked off too", Verdict(false, CatalogueSide.Server, true, SyncChoice.Sync).Mode == SyncMode.Off && Verdict(false, CatalogueSide.Server, false, SyncChoice.Auto).Locked);
Check("decide: the never-send list wins over everything", Verdict(true, CatalogueSide.Both, true, SyncChoice.Sync).Mode == SyncMode.Off && Verdict(true, CatalogueSide.None, true, SyncChoice.Sync).Locked);
Check("decide: every outcome says why, briefly", new[] { Verdict(true, CatalogueSide.None, true, SyncChoice.Auto), Verdict(false, CatalogueSide.Client, true, SyncChoice.Auto), Verdict(false, CatalogueSide.Server, true, SyncChoice.Auto), Verdict(false, CatalogueSide.None, true, SyncChoice.Warn), Verdict(false, CatalogueSide.None, true, SyncChoice.Auto), Verdict(false, CatalogueSide.None, false, SyncChoice.Auto) }.All(v => v.Why.Length > 0 && v.Why.Length <= 26));
Check("decide: the three words", SyncDecision.Word(SyncMode.Sync) == "sync" && SyncDecision.Word(SyncMode.Warn) == "warn" && SyncDecision.Word(SyncMode.Off) == "off");

var syncFile = new Dictionary<string, SyncChoice> { ["blueprinter.shippack1"] = SyncChoice.Warn, ["some.\"odd\" id"] = SyncChoice.Off, ["com.example.sync"] = SyncChoice.Sync, ["left.alone"] = SyncChoice.Auto };
string syncText = SyncChoices.Serialize(syncFile, id => id == "blueprinter.shippack1" ? "Blueprinter.shippack1" : "");
Dictionary<string, SyncChoice> syncRead = SyncChoices.Parse(syncText, out List<string> syncBad);
Check("choices file: written and read back", syncRead.Count == 3 && syncRead["blueprinter.shippack1"] == SyncChoice.Warn && syncRead["some.\"odd\" id"] == SyncChoice.Off && syncRead["com.example.sync"] == SyncChoice.Sync && syncBad.Count == 0);
Check("choices file: a mod with no choice is not written", !syncText.Contains("left.alone"));
Check("choices file: the mod's name is a note on its line", syncText.Contains("\"blueprinter.shippack1\": warn   # Blueprinter.shippack1"));
Check("choices file: bad lines are reported, good ones kept", SyncChoices.Parse("\"a\": sync\n\"b\": maybe\nrubbish\n\"c\": reset\n# note\n\n\"D\": OFF", out List<string> syncBad2).Count == 2 && syncBad2.Count == 3);
Check("choices file: ids are compared without capitals", SyncChoices.Parse("\"Mod.A\": Warn", out _)["mod.a"] == SyncChoice.Warn);
Check("choices file: empty text is no choices", SyncChoices.Parse(null, out _).Count == 0);
Check("choice words", SyncChoices.TryWord("SYNC", out SyncChoice w1) && w1 == SyncChoice.Sync && SyncChoices.TryWord("reset", out SyncChoice w2) && w2 == SyncChoice.Auto && !SyncChoices.TryWord("kick", out _));

var mixedMods = new[] { new SyncMod { Guid = "applies", Apply = true, Settings = { new SyncSetting { Key = "k", Value = "1" } } }, new SyncMod { Guid = "warns", Apply = false, Settings = { new SyncSetting { Key = "k", Value = "1" } } } };
List<SyncMod> mixedBack = ConfigPack.Read(ConfigPack.Write(mixedMods));
Check("pack: each mod carries whether it is applied or only compared", mixedBack[0].Apply && !mixedBack[1].Apply);

// ---- Stage 4c: the put-back guard ----
var guardWindow = new RevertWindow();
Check("guard window: counts events inside the window", guardWindow.Add(0, 10) == 1 && guardWindow.Add(4, 10) == 2 && guardWindow.Add(9, 10) == 3);
Check("guard window: old events fall out", guardWindow.Add(12, 10) == 3 && guardWindow.Add(30, 10) == 1);

var guardA = new GuardTracker();
Check("guard: below the count nothing happens", guardA.Report("s", RevertKind.PutBack, 0, 3, 10, out _) == GuardAction.None && guardA.Report("s", RevertKind.PutBack, 1, 3, 10, out _) == GuardAction.None);
Check("guard: at the count the server sends a stop", guardA.Report("s", RevertKind.PutBack, 2, 3, 10, out string guardNone) == GuardAction.SendStop && guardNone == null);
Check("guard: put-backs still in flight after the stop are ignored", guardA.Report("s", RevertKind.PutBack, 2.1, 3, 10, out _) == GuardAction.None);
Check("guard: the acknowledgement settles it", guardA.Report("s", RevertKind.Ack, 2.5, 3, 10, out string guardAck) == GuardAction.Settled && guardAck == GuardTracker.ByServer);
Check("guard: nothing more happens to a settled setting", guardA.Report("s", RevertKind.PutBack, 3, 3, 10, out _) == GuardAction.None && guardA.Report("s", RevertKind.Ack, 3, 3, 10, out _) == GuardAction.None && guardA.Report("s", RevertKind.SafetyNet, 3, 3, 10, out _) == GuardAction.None && guardA.Overdue(999).Count == 0);

var guardB = new GuardTracker();
guardB.Report("s", RevertKind.PutBack, 0, 1, 10, out _);
Check("guard race: the safety net arriving while a stop is waiting settles it as the safety net", guardB.Report("s", RevertKind.SafetyNet, 0.2, 1, 10, out string guardNet) == GuardAction.Settled && guardNet == GuardTracker.BySafetyNet);
Check("guard race: the acknowledgement that follows is ignored, and nobody is dropped", guardB.Report("s", RevertKind.Ack, 0.3, 1, 10, out _) == GuardAction.None && guardB.Overdue(999).Count == 0);

var guardC = new GuardTracker();
Check("guard: the safety net before the server reached its count", guardC.Report("s", RevertKind.SafetyNet, 0, 3, 10, out string guardNet2) == GuardAction.Settled && guardNet2 == GuardTracker.BySafetyNet && guardC.Report("s", RevertKind.PutBack, 1, 1, 10, out _) == GuardAction.None);
Check("guard: an acknowledgement nobody asked for is ignored", new GuardTracker().Report("s", RevertKind.Ack, 0, 3, 10, out _) == GuardAction.None);

var guardD = new GuardTracker();
guardD.Report("one", RevertKind.PutBack, 0, 1, 10, out _);
guardD.Report("two", RevertKind.PutBack, 20, 1, 10, out _);
Check("guard: no answer is not overdue before the wait is up", guardD.Overdue(29).Count == 0);
List<string> guardLate = guardD.Overdue(30);
Check("guard: after the wait the unanswered stop is overdue, per setting", guardLate.Count == 1 && guardLate[0] == "one");
Check("guard: an overdue setting is settled and reported once", guardD.Overdue(31).Count == 0 && guardD.Report("one", RevertKind.Ack, 32, 1, 10, out _) == GuardAction.None && guardD.Overdue(50).Count == 1);
Check("guard: settings are counted separately", new Func<bool>(() => { var g = new GuardTracker(); g.Report("a", RevertKind.PutBack, 0, 2, 10, out _); return g.Report("b", RevertKind.PutBack, 1, 2, 10, out _) == GuardAction.None && g.Report("a", RevertKind.PutBack, 2, 2, 10, out _) == GuardAction.SendStop; })());
Check("guard: slow changes spread wider than the window never trip it", new Func<bool>(() => { var g = new GuardTracker(); for (int i = 0; i < 50; i++) if (g.Report("a", RevertKind.PutBack, i * 6, 3, 10, out _) != GuardAction.None) return false; return true; })());
Check("guard: the three result lines", GuardTracker.ByServer == "Stopped by server; acknowledged." && GuardTracker.BySafetyNet == "Stopped by client safety net." && GuardTracker.Dropped == "No acknowledgement; player dropped.");

// ---- Stage 4c: the list of stopped settings and the notices an admin hid ----
var stopLog = new StopLog();
uint stopOne = stopLog.Add("2026-11-23 14:32  Ann (1)  Mod / S.K  Stopped by server; acknowledged."), stopTwo = stopLog.Add("second\nline");
Check("stop list: entries are numbered from 1 and never share a number", stopOne == 1 && stopTwo == 2 && stopLog.Newest == 2);
StopLog stopBack = StopLog.Parse(stopLog.Serialize());
Check("stop list: written and read back, one line per entry", stopBack.Entries.Count == 2 && stopBack.Entries[0].Value.EndsWith("acknowledged.") && stopBack.Entries[1].Value == "second line" && stopBack.Next == 3);
Check("stop list: removing one leaves the rest and the numbering", stopBack.Remove(1) && !stopBack.Remove(1) && stopBack.Entries.Count == 1 && stopBack.Add("third") == 3);
Check("stop list: clearing empties it but numbers keep counting", stopBack.Clear() == 2 && stopBack.Entries.Count == 0 && StopLog.Parse(stopBack.Serialize()).Add("x") == 4);
var stopFull = new StopLog(); for (int i = 0; i < 60; i++) stopFull.Add("e" + i);
Check("stop list: only the newest 50 are kept", stopFull.Entries.Count == 50 && stopFull.Entries[0].Value == "e10" && stopFull.Newest == 60);
Check("stop list: a hand-edited file cannot make numbers repeat", StopLog.Parse("next: 1\n7|seven\nrubbish\n|x\n0|zero").Next == 8 && StopLog.Parse("next: 1\n7|seven\nrubbish").Entries.Count == 1 && StopLog.Parse(null).Entries.Count == 0);

var noticeSeen = new NoticeSeen();
var seenLog = new StopLog(); seenLog.Add("a");
Check("notices: shown until the admin hides them", !noticeSeen.StopsQuiet("111", seenLog) && !noticeSeen.LeftoversQuiet("111", new[] { "x.dll" }));
noticeSeen.QuietStops("111", seenLog); noticeSeen.QuietLeftovers("111", new[] { "x.dll" });
Check("notices: hidden for that admin only", noticeSeen.StopsQuiet("111", seenLog) && noticeSeen.LeftoversQuiet("111", new[] { "x.dll" }) && !noticeSeen.StopsQuiet("222", seenLog) && !noticeSeen.LeftoversQuiet("222", new[] { "x.dll" }));
seenLog.Add("b");
Check("notices: another stop brings the stops notice back", !noticeSeen.StopsQuiet("111", seenLog));
Check("notices: a different set of leftover files brings that notice back; the same set in another order does not", !noticeSeen.LeftoversQuiet("111", new[] { "x.dll", "y.dll" }) && NoticeSeen.Print(new[] { "a", "b" }) == NoticeSeen.Print(new[] { "b", "a" }) && NoticeSeen.Print(null) == "");
noticeSeen.QuietStops("111", seenLog); seenLog.Remove(1);
Check("notices: removing an entry does not bring the notice back", noticeSeen.StopsQuiet("111", seenLog));
uint seenNew = seenLog.Add("c");
Check("notices: a stop recorded while the admin was away brings the notice back", !noticeSeen.StopsQuiet("111", seenLog));
seenLog.Remove(seenNew);
Check("notices: removing that new entry hides the notice again; the older entries were already hidden", noticeSeen.StopsQuiet("111", seenLog));
seenLog.Clear();
Check("notices: with nothing in the list there is nothing to hide", !noticeSeen.StopsQuiet("111", seenLog) && !noticeSeen.LeftoversQuiet("111", new string[0]));
noticeSeen.QuietStops("the host", seenLog);
NoticeSeen seenBack = NoticeSeen.Parse(noticeSeen.Serialize());
Check("notices: written and read back, including an admin with a space in the name", seenBack.Of("111").Stops == 2 && seenBack.Of("111").Leftovers == NoticeSeen.Print(new[] { "x.dll" }) && seenBack.ByAdmin.ContainsKey("the host") && NoticeSeen.Parse("# note\n\nbad").ByAdmin.Count == 1);

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
