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
using System.Numerics;
using Microsoft.Extensions.Configuration;

namespace GenOnlineService
{
	/// <summary>
	/// Enforces minimum client version requirements for game client logins.
	/// </summary>
	public static class ClientVersionPolicy
	{
		/// <summary>
		/// Evaluates whether the specified client version is allowed under current server policy.
		/// </summary>
		/// <param name="clientVersion">The client version reported by the game client.</param>
		/// <param name="reason">The outcome description or rejection reason.</param>
		/// <returns>True if the client is permitted to log in, false if rejected.</returns>
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
				reason = "Server configuration error: MinVersion is empty while enforcement is enabled";
				return false;
			}

			if (!TryParseVersion(minVersionStr, out Version? minVer, out string? minPrerelease) || minVer == null)
			{
				reason = $"Server configuration error: MinVersion '{minVersionStr}' cannot be parsed";
				return false;
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

			if (!TryParseVersion(clientVersion, out Version? clientVer, out string? clientPrerelease) || clientVer == null)
			{
				reason = $"Client version '{clientVersion}' cannot be parsed";
				return false;
			}

			if (clientVer < minVer)
			{
				reason = $"Client version {clientVer} is lower than required minimum {minVer}";
				return false;
			}

			// In SemVer, a version with a prerelease tag has lower precedence than a normal release.
			if (clientVer == minVer)
			{
				if (!string.IsNullOrEmpty(clientPrerelease) && string.IsNullOrEmpty(minPrerelease))
				{
					reason = $"Client prerelease version '{clientVersion}' does not satisfy minimum release version '{minVersionStr}'";
					return false;
				}

				if (!string.IsNullOrEmpty(clientPrerelease) && !string.IsNullOrEmpty(minPrerelease))
				{
					if (ComparePrerelease(clientPrerelease, minPrerelease) < 0)
					{
						reason = $"Client prerelease '{clientPrerelease}' is lower than required minimum prerelease '{minPrerelease}'";
						return false;
					}
				}
			}

			reason = "Version allowed";
			return true;
		}

		/// <summary>
		/// Attempts to parse a version string into a <see cref="Version"/> object and extracts any prerelease suffix.
		/// </summary>
		/// <param name="versionStr">The version string to parse.</param>
		/// <param name="version">The parsed <see cref="Version"/> object, or null if parsing failed.</param>
		/// <param name="prerelease">The extracted prerelease tag, or null if none.</param>
		/// <returns>True if the numeric version was successfully parsed, false otherwise.</returns>
		public static bool TryParseVersion(string? versionStr, out Version? version, out string? prerelease)
		{
			version = null;
			prerelease = null;
			if (string.IsNullOrWhiteSpace(versionStr))
			{
				return false;
			}

			string cleaned = versionStr.Trim();
			if (cleaned.StartsWith("v", StringComparison.OrdinalIgnoreCase))
			{
				cleaned = cleaned.Substring(1);
			}

			// Strip build metadata (+) first if present
			int plusIdx = cleaned.IndexOf('+');
			if (plusIdx >= 0)
			{
				cleaned = cleaned.Substring(0, plusIdx);
			}

			// Extract prerelease suffix (-)
			int dashIdx = cleaned.IndexOf('-');
			if (dashIdx >= 0)
			{
				prerelease = cleaned.Substring(dashIdx + 1);
				cleaned = cleaned.Substring(0, dashIdx);
			}

			string[] parts = cleaned.Split('.');
			if (parts.Length == 1)
			{
				cleaned += ".0";
			}

			return Version.TryParse(cleaned, out version);
		}

		/// <summary>
		/// Attempts to parse a version string into a <see cref="Version"/> object.
		/// </summary>
		/// <param name="versionStr">The version string to parse.</param>
		/// <param name="version">The parsed <see cref="Version"/> object, or null if parsing failed.</param>
		/// <returns>True if parsing was successful, false otherwise.</returns>
		public static bool TryParseVersion(string? versionStr, out Version? version)
		{
			return TryParseVersion(versionStr, out version, out _);
		}

		/// <summary>
		/// Compares two SemVer 2.0.0 prerelease identifier strings according to SemVer precedence rules.
		/// </summary>
		/// <param name="preA">The first prerelease string.</param>
		/// <param name="preB">The second prerelease string.</param>
		/// <returns>A negative integer if preA &lt; preB, zero if preA == preB, or a positive integer if preA &gt; preB.</returns>
		public static int ComparePrerelease(string preA, string preB)
		{
			if (string.Equals(preA, preB, StringComparison.OrdinalIgnoreCase))
			{
				return 0;
			}

			string[] aParts = preA.Split('.');
			string[] bParts = preB.Split('.');
			int len = Math.Min(aParts.Length, bParts.Length);

			for (int i = 0; i < len; i++)
			{
				string partA = aParts[i];
				string partB = bParts[i];
				if (string.Equals(partA, partB, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				bool aIsNum = BigInteger.TryParse(partA, out BigInteger numA);
				bool bIsNum = BigInteger.TryParse(partB, out BigInteger numB);

				if (aIsNum && bIsNum)
				{
					int numCmp = numA.CompareTo(numB);
					if (numCmp != 0)
					{
						return numCmp;
					}
				}
				else if (aIsNum && !bIsNum)
				{
					// Numeric identifiers have lower precedence than non-numeric identifiers
					return -1;
				}
				else if (!aIsNum && bIsNum)
				{
					return 1;
				}
				else
				{
					int strCmp = string.Compare(partA, partB, StringComparison.OrdinalIgnoreCase);
					if (strCmp != 0)
					{
						return strCmp;
					}
				}
			}

			return aParts.Length.CompareTo(bParts.Length);
		}
	}
}
