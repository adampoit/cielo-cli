{self}: {
  config,
  lib,
  pkgs,
  ...
}: let
  cfg = config.services.cielo-monitor;
  effectiveHistoryFile = cfg.historyFile or "${cfg.dataDir}/history.ndjson";
  historyDir = builtins.dirOf effectiveHistoryFile;
  readWritePaths = lib.unique [
    cfg.dataDir
    historyDir
  ];
  hasWeather = cfg.weather.latitude != null && cfg.weather.longitude != null;
  execArgs =
    [
      "${lib.getExe cfg.package}"
      "monitor"
      "--daemon"
      "--config"
      cfg.configFile
      "--interval"
      (toString cfg.interval)
      "--history-file"
      effectiveHistoryFile
    ]
    ++ lib.optionals (cfg.device != null) [
      "--device"
      cfg.device
    ]
    ++ lib.optionals cfg.changesOnly ["--changes-only"]
    ++ lib.optionals cfg.dryRun ["--dry-run"]
    ++ lib.optionals (cfg.rulesFile != null) [
      "--rules"
      cfg.rulesFile
    ]
    ++ lib.optionals hasWeather [
      "--weather-lat"
      (toString cfg.weather.latitude)
      "--weather-lon"
      (toString cfg.weather.longitude)
      "--weather-refresh-minutes"
      (toString cfg.weather.refreshMinutes)
    ]
    ++ cfg.extraArgs;
  execStart = lib.concatStringsSep " " (map lib.escapeShellArg execArgs);
in {
  options.services.cielo-monitor = {
    enable = lib.mkEnableOption "the cielo monitor daemon";

    package = lib.mkOption {
      type = lib.types.package;
      default = self.packages.${pkgs.system}.default;
      description = "The cielo package to run.";
    };

    dataDir = lib.mkOption {
      type = lib.types.str;
      default = "/var/lib/cielo";
      description = "Directory used for monitor history and runtime-owned files.";
    };

    configFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/run/secrets/cielo-config.json";
      description = "Path to the Cielo auth config JSON consumed by the CLI.";
    };

    rulesFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/etc/cielo/rules.json";
      description = "Optional rules JSON file passed to `cielo monitor --rules`.";
    };

    historyFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/var/lib/cielo/history.ndjson";
      description = "Optional path for NDJSON history output. Defaults under `dataDir`.";
    };

    interval = lib.mkOption {
      type = lib.types.ints.positive;
      default = 60;
      description = "Polling interval in seconds.";
    };

    device = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      description = "Optional device name, MAC address, or appliance id to monitor.";
    };

    changesOnly = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = "Only emit samples when the monitored values change.";
    };

    dryRun = lib.mkOption {
      type = lib.types.bool;
      default = false;
      description = "Evaluate rules without executing hooks.";
    };

    weather = {
      latitude = lib.mkOption {
        type = lib.types.nullOr lib.types.float;
        default = null;
        description = "Optional latitude for Open-Meteo lookups.";
      };

      longitude = lib.mkOption {
        type = lib.types.nullOr lib.types.float;
        default = null;
        description = "Optional longitude for Open-Meteo lookups.";
      };

      refreshMinutes = lib.mkOption {
        type = lib.types.ints.positive;
        default = 15;
        description = "How often to refresh outdoor weather data.";
      };
    };

    extraArgs = lib.mkOption {
      type = lib.types.listOf lib.types.str;
      default = [];
      description = "Additional arguments appended to `cielo monitor --daemon`.";
    };

    environment = lib.mkOption {
      type = lib.types.attrsOf lib.types.str;
      default = {};
      description = "Additional environment variables for the monitor service.";
    };

    environmentFile = lib.mkOption {
      type = lib.types.nullOr lib.types.str;
      default = null;
      example = "/run/secrets/cielo-monitor.env";
      description = "Optional environment file for extra service variables.";
    };
  };

  config = lib.mkIf cfg.enable {
    assertions = [
      {
        assertion = cfg.configFile != null;
        message = "services.cielo-monitor.configFile must be set.";
      }
      {
        assertion = (cfg.weather.latitude == null) == (cfg.weather.longitude == null);
        message = "services.cielo-monitor.weather.latitude and weather.longitude must be set together.";
      }
    ];

    users.groups.cielo = {};

    users.users.cielo = {
      isSystemUser = true;
      group = "cielo";
      home = cfg.dataDir;
      createHome = false;
    };

    systemd.tmpfiles.rules =
      ["d ${cfg.dataDir} 0750 cielo cielo -"]
      ++ lib.optional (historyDir != cfg.dataDir) "d ${historyDir} 0750 cielo cielo -";

    systemd.services.cielo-monitor = {
      description = "Cielo monitor";
      after = ["network-online.target"];
      wants = ["network-online.target"];
      wantedBy = ["multi-user.target"];
      environment = cfg.environment;
      serviceConfig =
        {
          Type = "notify";
          User = "cielo";
          Group = "cielo";
          WorkingDirectory = cfg.dataDir;
          ExecStart = execStart;
          Restart = "always";
          RestartSec = 5;
          NoNewPrivileges = true;
          PrivateTmp = true;
          ProtectHome = true;
          ProtectSystem = "strict";
          ReadWritePaths = readWritePaths;
        }
        // lib.optionalAttrs (cfg.environmentFile != null) {
          EnvironmentFile = cfg.environmentFile;
        };
    };
  };
}
