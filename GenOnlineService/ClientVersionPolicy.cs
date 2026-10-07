/*
**    GeneralsOnline Game Services - Backend Services for Command & Conquer Generals Online: Zero Hour
**    Copyright (C) 2025  GeneralsOnline Development Team
**
**    This program is free software: you can redistribute it and/or modify
**    it under the terms of the GNU Affero General Public License as
**    published by the Free Software Foundation, either version 3 of the
**    License, or (at your option) any later version.
**
**    This program is distributed in the hope that it will be useful,
**    but WITHOUT ANY WARRANTY; without even the implied warranty of
**    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
**    GNU Affero General Public License for more details.
**
**    You should have received a copy of the GNU Affero General Public License
**    along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using Microsoft.Extensions.Configuration;

namespace GenOnlineService
{
	public static class ClientVersionPolicy
	{
		public static bool IsVersionAllowed(string? clientVersion, out string reason)
		{
			var section = Program.g_Config?.GetSection("ClientPolicy");
			if (section == null)
			{
				reason = "No ClientPolicy section found";
				return true;
			}

			bool enforce = section.GetValue<bool>("EnforceMinVersion", false);
			if (!enforce)
			{
				reason = "EnforceMinVersion disabled";
				return true;
			}

			string? minVersionStr = section.GetValue<string>("MinVersion");
			if (string.IsNullOrWhiteSpace(minVersionStr))
			{
				reason = "MinVersion is empty";
				return true;
			}

			bool allowDev = section.GetValue<bool>("AllowDevClients", true);
			if (allowDev && !string.IsNullOrWhiteSpace(clientVersion) &&
				(clientVersion.Equals("dev", StringComparison.OrdinalIgnoreCase) ||
				 clientVersion.Equals("development", StringComparison.OrdinalIgnoreCase)))
			{
				reason = "Dev client allowed";
				return true;
			}

			if (string.IsNullOrWhiteSpace(clientVersion))
			{
				reason = "Client version is missing";
				return false;
			}

			if (!TryParseVersion(clientVersion, out Version? clientVer) || clientVer == null)
			{
				reason = $"Client version '{clientVersion}' cannot be parsed";
				return false;
			}

			if (!TryParseVersion(minVersionStr, out Version? minVer) || minVer == null)
			{
				reason = $"Server MinVersion '{minVersionStr}' cannot be parsed";
				return true;
			}

			if (clientVer < minVer)
			{
				reason = $"Client version {clientVer} is lower than required minimum {minVer}";
				return false;
			}

			reason = "Version allowed";
			return true;
		}

		public static bool TryParseVersion(string versionStr, out Version? version)
		{
			version = null;
			if (string.IsNullOrWhiteSpace(versionStr))
			{
				return false;
			}

			string cleaned = versionStr.Trim();
			if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
			{
				cleaned = cleaned.Substring(1);
			}

			int dashIdx = cleaned.IndexOfAny(new[] { '-', '+' });
			if (dashIdx > 0)
			{
				cleaned = cleaned.Substring(0, dashIdx);
			}

			string[] parts = cleaned.Split('.');
			if (parts.Length == 1)
			{
				cleaned += ".0";
			}

			return Version.TryParse(cleaned, out version);
		}
	}
}
