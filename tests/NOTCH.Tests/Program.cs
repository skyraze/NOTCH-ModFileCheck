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

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
