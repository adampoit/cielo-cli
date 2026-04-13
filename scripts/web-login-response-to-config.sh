#!/usr/bin/env bash
set -euo pipefail

if ! command -v jq >/dev/null 2>&1; then
	echo "error: jq is required" >&2
	exit 1
fi

usage() {
	cat <<'EOF'
Usage: web-login-response-to-config.sh [login-response.json]

Reads a Cielo Home web login response from a file or stdin and prints a
config.json-compatible object.

Optional environment variables:
  CIELO_WEB_X_API_KEY   Override x_api_key when it is not present in the response
EOF
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
	usage
	exit 0
fi

input=${1:-/dev/stdin}

jq \
	--arg web_x_api_key "${CIELO_WEB_X_API_KEY:-}" \
	'
  def pick($paths):
    first($paths[] as $path | getpath($path) | select(. != null and . != "")) // "";

  {
    access_token: pick([
      ["data", "accessToken"],
      ["data", "access_token"],
      ["data", "user", "accessToken"],
      ["data", "user", "access_token"]
    ]),
    refresh_token: pick([
      ["data", "refreshToken"],
      ["data", "refresh_token"],
      ["data", "user", "refreshToken"],
      ["data", "user", "refresh_token"]
    ]),
    session_id: pick([
      ["data", "sessionId"],
      ["data", "session_id"],
      ["data", "user", "sessionId"],
      ["data", "user", "session_id"]
    ]),
    user_id: pick([
      ["data", "userId"],
      ["data", "user_id"],
      ["data", "user", "userId"],
      ["data", "user", "user_id"]
    ]),
		x_api_key: (pick([
		  ["data", "xApiKey"],
      ["data", "x_api_key"],
      ["data", "user", "xApiKey"],
		  ["data", "user", "x_api_key"]
		]) // "") as $from_response
		  | if $from_response != "" then $from_response else $web_x_api_key end
	  }
	  ' "$input" |
	jq -e '
      if (.access_token == "" or .refresh_token == "" or .session_id == "" or .user_id == "" or .x_api_key == "")
      then error("missing one or more required values: access_token, refresh_token, session_id, user_id, x_api_key")
      else .
      end
    '
