using CieloCli.Models;

namespace CieloCli.Services;

internal static class CieloDeviceResolver
{
	public static CieloDevice Resolve(IReadOnlyList<CieloDevice> devices, string identifier)
	{
		var normalized = identifier.Trim();

		var exactMac = devices.FirstOrDefault(device =>
			string.Equals(device.MacAddress, normalized, StringComparison.OrdinalIgnoreCase)
		);
		if (exactMac is not null)
		{
			return exactMac;
		}

		if (long.TryParse(normalized, out var applianceId))
		{
			var byApplianceId = devices.Where(device => device.ApplianceId == applianceId).ToList();
			if (byApplianceId.Count == 1)
			{
				return byApplianceId[0];
			}

			if (byApplianceId.Count > 1)
			{
				throw new InvalidOperationException(
					$"Appliance id '{identifier}' matches multiple devices. Use the MAC address instead."
				);
			}
		}

		var byName = devices
			.Where(device =>
				string.Equals(device.DeviceName, normalized, StringComparison.OrdinalIgnoreCase)
			)
			.ToList();

		if (byName.Count == 1)
		{
			return byName[0];
		}

		if (byName.Count > 1)
		{
			throw new InvalidOperationException(
				$"Device name '{identifier}' matches multiple devices. Use the MAC address instead."
			);
		}

		throw new InvalidOperationException($"Device '{identifier}' was not found.");
	}
}
