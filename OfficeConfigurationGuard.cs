using System.Windows;

namespace TNovClient
{
    internal static class OfficeConfigurationGuard
    {
        public static bool TrySwitchIfWrongOffice()
        {
            TNovConfig current = files.LoadConfig();
            if (current == null || !IsCorp(current))
                return false;

            List<(string DisplayName, TNovConfig Config)> offices = OfficeServers.GetAll().ToList();
            string currentMarker = ServerProbe.MarkerPath(current.ServerPath);
            Dictionary<string, TimedProbe> currentResults = ServerProbe.ProbeMany(
                new[] { currentMarker },
                ServerProbe.Timeout);
            currentResults.TryGetValue(currentMarker, out TimedProbe currentProbe);

            // Свой сервер отвечает быстро — чужой офис не трогаем, чтобы не ждать SMB-таймаут.
            if (currentProbe.Reachable && currentProbe.Elapsed < ServerProbe.SlowThreshold)
                return false;

            (string DisplayName, TNovConfig Config, TimedProbe Probe)? bestAlt = null;
            var altMarkers = new List<string>();
            foreach ((_, TNovConfig office) in offices)
            {
                if (OfficeServers.PathsMatch(office.ServerPath, current.ServerPath))
                    continue;
                altMarkers.Add(ServerProbe.MarkerPath(office.ServerPath));
            }

            Dictionary<string, TimedProbe> altResults = ServerProbe.ProbeMany(altMarkers, ServerProbe.Timeout);
            foreach ((string name, TNovConfig office) in offices)
            {
                if (OfficeServers.PathsMatch(office.ServerPath, current.ServerPath))
                    continue;

                string marker = ServerProbe.MarkerPath(office.ServerPath);
                if (!altResults.TryGetValue(marker, out TimedProbe probe) || !probe.Reachable)
                    continue;

                if (bestAlt == null || probe.Elapsed < bestAlt.Value.Probe.Elapsed)
                    bestAlt = (name, office, probe);
            }

            if (bestAlt == null || !LooksLikeWrongOffice(currentProbe, bestAlt.Value.Probe))
                return false;

            string currentName = OfficeServers.GetDisplayName(current)
                ?? ClientPaths.NormalizeServerPath(current.ServerPath);

            MessageBoxResult answer = MessageBox.Show(
                $"Клиент слишком долго подключается к серверу «{currentName}».\n" +
                "Похоже, используется конфигурация другого офиса.\n\n" +
                $"Переключить на «{bestAlt.Value.DisplayName}»?",
                "TNovClient",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return false;

            if (!files.SaveConfig(bestAlt.Value.Config))
                return false;

            ServerProbe.Invalidate(currentMarker);
            ServerProbe.RememberReachable(ServerProbe.MarkerPath(bestAlt.Value.Config.ServerPath));
            return true;
        }

        private static bool LooksLikeWrongOffice(TimedProbe current, TimedProbe alternative)
        {
            if (!current.Reachable)
                return true;

            return current.Elapsed >= ServerProbe.SlowThreshold
                && alternative.Elapsed > TimeSpan.Zero
                && current.Elapsed > TimeSpan.FromMilliseconds(alternative.Elapsed.TotalMilliseconds * 3);
        }

        private static bool IsCorp(TNovConfig config) =>
            string.Equals(config.LicenseType, "corp", StringComparison.OrdinalIgnoreCase);
    }
}
