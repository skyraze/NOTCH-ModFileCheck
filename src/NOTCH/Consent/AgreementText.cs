namespace NOTCH.Consent
{
    // The notice texts, by version. A released text is never edited: a change to the wording is a new version.
    public static partial class Agreement
    {
        private static readonly AgreementVersion[] Versions =
        {
            new AgreementVersion("0.13.0", true, Text_0_13_0),
        };

        private const string Text_0_13_0 = @"================================================================================
NOTCH NETWORK ACCESS NOTICE AND CONSENT
Agreement Version: 0.13.0
================================================================================

1. PURPOSE AND SCOPE
   This Network Access Notice and Consent (""Notice"") governs the network
   communication features of the NOTCH software (""NOTCH""). NOTCH includes
   functionality allowing it to query NOMNOM, a publicly accessible mod
   catalogue hosted via GitHub, to determine mod compatibility
   classifications (specifically, whether a given Nuclear Option mod is
   designated for clients, servers, or both). This Notice outlines the
   parameters of such network access and requests your affirmative consent
   prior to enabling this functionality.

2. AUTHORIZED NETWORK ACTIVITY
   By granting consent, you authorize NOTCH, solely when operating as a
   dedicated server or hosting a multiplayer session, to establish outbound
   network connections to GitHub to retrieve the NOMNOM catalogue. This
   retrieval occurs upon server initialization and periodically thereafter in
   the background during active runtime, at intervals of no less than
   approximately one hour. NOTCH shall not initiate these network requests when
   you are solely connected as a client to an external server.

3. DATA TRANSMISSION, LOCAL RETENTION, AND PRIVACY
   NOTCH transmits no telemetry, personally identifiable information, player
   data, installed mod lists, or local configuration settings during this
   process. NOTCH retrieves the complete catalogue and parses the metadata
   locally on your host machine, ensuring that outbound requests do not disclose
   which specific mods are installed. Following parsing, NOTCH retains locally
   only a reduced copy of the catalogue: the metadata relevant to the mods
   present on your machine, together with the catalogue's list of mod
   identifiers and the size of the retrieved file, which are used solely to
   validate subsequent retrievals. You acknowledge that, inherent to standard
   internet protocols (TCP/IP and HTTP/S), GitHub will receive your public
   Internet Protocol (IP) address and standard request headers, and may log or
   process such connection metadata in accordance with GitHub's applicable Terms
   of Service and Privacy Statement.

4. LIMITATIONS OF FUNCTIONALITY
   Consent granted under this Notice strictly limits NOTCH to reading
   passive catalogue metadata. NOTCH does not download, install, modify,
   update, or execute any third-party mods, binaries, or executable code
   pursuant to this authorization.

5. THIRD-PARTY SERVICES AND DISCLAIMER OF AFFILIATION
   NOMNOM is an independent, community-maintained repository, and GitHub is
   a hosting platform operated by GitHub, Inc. Neither NOMNOM nor GitHub,
   Inc. is owned, operated, endorsed by, or affiliated with NOTCH or its
   author(s). The author(s) of NOTCH assume no responsibility for the
   operation or data handling practices of these third-party services.

6. USER ELECTION AND REVOCATION OF CONSENT
   Your election to grant or withhold consent is entirely voluntary and will
   not impair the core functionality of NOTCH:

   ACCEPT:  You affirmatively consent to NOTCH initiating outbound requests
            to retrieve the NOMNOM catalogue as set forth in this Notice.
   DECLINE: NOTCH will initiate no external network connections and will
            rely exclusively on local metadata embedded within your
            installed mods.

   You may revoke or modify your election at any time by updating the
   relevant consent parameter within NOTCH's local configuration file.

7. MODIFICATIONS TO THIS NOTICE
   In the event of material revisions to the terms or scope of this Notice,
   any prior consent shall be deemed superseded, and NOTCH will prompt you
   for renewed affirmative consent prior to initiating any subsequent
   network access.

8. DISCLAIMER OF WARRANTIES (""AS-IS"")
   NOTCH AND THIS NETWORK ACCESS FEATURE ARE PROVIDED ON AN ""AS IS"" AND ""AS
   AVAILABLE"" BASIS, WITHOUT WARRANTIES OF ANY KIND, EXPRESS OR IMPLIED,
   INCLUDING, BUT NOT LIMITED TO, IMPLIED WARRANTIES OF MERCHANTABILITY,
   FITNESS FOR A PARTICULAR PURPOSE, ACCURACY, AND NON-INFRINGEMENT. THE
   AUTHOR(S) DO NOT CONTROL NOMNOM OR GITHUB AND MAKE NO REPRESENTATION OR
   WARRANTY THAT THE CATALOGUE WILL BE ACCURATE, COMPLETE, UNINTERRUPTED,
   AVAILABLE, OR FREE FROM ERROR OR TAMPERING.

9. LIMITATION OF LIABILITY
   TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, IN NO EVENT SHALL THE
   AUTHOR(S) OR CONTRIBUTORS OF NOTCH BE LIABLE FOR ANY CLAIM, LOSS, OR
   DAMAGE OF ANY KIND--WHETHER DIRECT, INDIRECT, INCIDENTAL, CONSEQUENTIAL,
   OR SPECIAL--ARISING OUT OF OR IN CONNECTION WITH THIS FEATURE, INCLUDING
   ANY LOSS CAUSED BY THE CONTENT, UNAVAILABILITY, COMPROMISE, OR SECURITY
   BREACH OF ANY THIRD-PARTY SERVICE (INCLUDING, WITHOUT LIMITATION, NOMNOM
   AND GITHUB). YOU USE THIS FEATURE AT YOUR SOLE RISK.

10. SOFTWARE LICENSE
    NOTCH is licensed under the MIT License, Copyright (c) 2026 skyraze.
    Subject to the terms of that license, permission is granted, free of
    charge, to any person obtaining a copy of this software to use, copy,
    modify, merge, publish, distribute, sublicense, and/or sell copies of
    the software, provided that the copyright notice and permission notice
    are included in all copies or substantial portions of the software. The
    MIT License further provides, in part:

    THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
    IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY
    CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
    TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
    SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

    The complete text of the MIT License is distributed with NOTCH's source
    code. This Notice supplements, and does not supersede, modify, or limit,
    the terms of the MIT License.
================================================================================
";
    }
}
