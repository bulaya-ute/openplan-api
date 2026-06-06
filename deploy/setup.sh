#!/usr/bin/env bash
# =============================================================================
# OpenPlan Setup Script for Linux  v0.1.0
# https://github.com/bulaya-ute/openplan-api
#
# Usage:
#   bash setup.sh            # full interactive setup
#   bash setup.sh --dry-run  # print actions without executing
#   bash setup.sh --reset    # clear saved progress and restart from scratch
# =============================================================================
set -uo pipefail
IFS=$'\n\t'

SETUP_VERSION="0.2.0"
LOG="/tmp/openplan-setup.log"
STATE="/tmp/openplan-setup.state"
CONF="/tmp/openplan-setup.conf"
DRY_RUN=false

# GitHub repos
API_REPO="https://github.com/bulaya-ute/openplan-api.git"
WEB_REPO="https://github.com/bulaya-ute/openplan-web.git"
ADMIN_REPO="https://github.com/bulaya-ute/openplan-admin.git"

# ── Colours ───────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; CYAN='\033[0;36m'; BOLD='\033[1m'; NC='\033[0m'

# ── Output helpers ────────────────────────────────────────────────────────────
log()  { printf '[%s] %s\n' "$(date '+%Y-%m-%d %H:%M:%S')" "$*" >> "$LOG"; }
info() { printf "${CYAN}  →  %s${NC}\n" "$*";        log "INFO  $*"; }
ok()   { printf "${GREEN}  ✓  %s${NC}\n" "$*";       log "OK    $*"; }
warn() { printf "${YELLOW}  ⚠  %s${NC}\n" "$*";      log "WARN  $*"; }
err()  { printf "${RED}  ✗  %s${NC}\n" "$*" >&2;    log "ERROR $*"; }
die()  { err "$*"; exit 1; }
h1()   { printf "\n${BOLD}${BLUE}  ══  %s  ══${NC}\n\n" "$*"; log "--- $* ---"; }
h2()   { printf "\n${BOLD}  %s${NC}\n" "$*"; }

# ── Prompt helpers ─────────────────────────────────────────────────────────────
# All prompt output goes to stderr; result stored in _VAL (never stdout).
# This avoids stdout contamination when callers use $(...) capture.
_VAL=""

ask() {
    local label="$1" default="${2:-}"
    printf "${CYAN}  %s" "$label" >&2
    [[ -n "$default" ]] && printf " [%s]" "$default" >&2
    printf ": ${NC}" >&2
    read -r _VAL </dev/tty
    _VAL="${_VAL:-$default}"
}

ask_secret() {
    local label="$1"
    printf "${CYAN}  %s: ${NC}" "$label" >&2
    read -rs _VAL </dev/tty
    printf '\n' >&2
}

ask_yn() {
    local label="${1:-Continue?}"
    printf "${YELLOW}  %s [y/N] ${NC}" "$label" >&2
    local r
    read -r r </dev/tty
    [[ "$r" =~ ^[Yy]$ ]]
}

ask_choice() {
    # Numbered list. Stores chosen 1-based index in _VAL (default: 1).
    local label="$1"; shift
    local i=1
    printf "${CYAN}  %s${NC}\n" "$label" >&2
    for opt in "$@"; do
        printf "    ${BOLD}%d)${NC} %s\n" "$i" "$opt" >&2
        ((i++))
    done
    printf "${CYAN}  Choice [1]: ${NC}" >&2
    read -r _VAL </dev/tty
    _VAL="${_VAL:-1}"
}

gen_pass()   { openssl rand -base64 16 | tr -dc 'a-zA-Z0-9' | head -c 20; }
gen_secret() { openssl rand -base64 32; }

# ── run() — skips execution in dry-run mode ────────────────────────────────────
run() {
    log "RUN: $*"
    if $DRY_RUN; then
        printf "${YELLOW}  [dry-run] %s${NC}\n" "$*"
        return 0
    fi
    "$@"
}

# ── State tracking ─────────────────────────────────────────────────────────────
stage_done() { echo "$1" >> "$STATE"; log "DONE: $1"; }
stage_ran()  { grep -qx "$1" "$STATE" 2>/dev/null; }

# ── Config persistence ─────────────────────────────────────────────────────────
save_conf() {
    {
        printf 'CFG_API_DIR=%q\n'      "$CFG_API_DIR"
        printf 'CFG_WEB_DIR=%q\n'      "$CFG_WEB_DIR"
        printf 'CFG_ADMIN_DIR=%q\n'    "$CFG_ADMIN_DIR"
        printf 'CFG_UPDATER_DIR=%q\n'  "$CFG_UPDATER_DIR"
        printf 'CFG_API_SRC=%q\n'      "$CFG_API_SRC"
        printf 'CFG_WEB_SRC=%q\n'      "$CFG_WEB_SRC"
        printf 'CFG_ADMIN_SRC=%q\n'    "$CFG_ADMIN_SRC"
        printf 'CFG_API_PORT=%q\n'     "$CFG_API_PORT"
        printf 'CFG_UPDATER_PORT=%q\n' "$CFG_UPDATER_PORT"
        printf 'CFG_API_SVC=%q\n'      "$CFG_API_SVC"
        printf 'CFG_UPDATER_SVC=%q\n'  "$CFG_UPDATER_SVC"
        printf 'CFG_DB_HOST=%q\n'      "$CFG_DB_HOST"
        printf 'CFG_DB_PORT=%q\n'      "$CFG_DB_PORT"
        printf 'CFG_DB_NAME=%q\n'      "$CFG_DB_NAME"
        printf 'CFG_DB_USER=%q\n'      "$CFG_DB_USER"
        printf 'CFG_DB_PASS=%q\n'      "$CFG_DB_PASS"
        printf 'CFG_JWT_SECRET=%q\n'   "$CFG_JWT_SECRET"
        printf 'CFG_CORS_ORIGINS=%q\n'    "$CFG_CORS_ORIGINS"
        printf 'CFG_API_PUBLIC_URL=%q\n' "$CFG_API_PUBLIC_URL"
        printf 'CFG_GITHUB_REPO=%q\n'   "$CFG_GITHUB_REPO"
        printf 'CFG_GITHUB_TOKEN=%q\n' "$CFG_GITHUB_TOKEN"
        printf 'CFG_BACKUPS_DIR=%q\n'  "$CFG_BACKUPS_DIR"
    } > "$CONF"
    log "Config saved → $CONF"
}

load_conf() {
    # shellcheck source=/dev/null
    [[ -f "$CONF" ]] && source "$CONF" && log "Config loaded ← $CONF"
}

# ── Package manager detection (done once at startup) ──────────────────────────
_detect_pkg_mgr() {
    if   command -v apt-get &>/dev/null; then echo "apt"
    elif command -v dnf     &>/dev/null; then echo "dnf"
    elif command -v yum     &>/dev/null; then echo "yum"
    else echo "unknown"; fi
}
PKG_MGR=$(_detect_pkg_mgr)

_pkg_install() {
    case "$PKG_MGR" in
        apt) run apt-get install -y "$@" ;;
        dnf) run dnf install -y "$@" ;;
        yum) run yum install -y "$@" ;;
        *)   die "Cannot install packages — unsupported package manager." ;;
    esac
}

# ── Argument parsing ──────────────────────────────────────────────────────────
for _arg in "$@"; do
    case "$_arg" in
        --dry-run) DRY_RUN=true ;;
        --reset)   rm -f "$STATE" "$CONF"; info "Progress reset — starting fresh." ;;
        -h|--help)
            printf "Usage: bash %s [--dry-run] [--reset]\n\n" "$0"
            printf "  --dry-run   Print what would be done without executing\n"
            printf "  --reset     Discard saved progress and restart from scratch\n"
            exit 0 ;;
        *) warn "Unknown argument: $_arg" ;;
    esac
done

# ── Stdin check — cannot run interactively when piped ────────────────────────
if [[ ! -t 0 ]] && ! $DRY_RUN; then
    printf "${RED}ERROR:${NC} This script requires an interactive terminal.\n"
    printf "Download it first, then run:\n\n"
    printf "  curl -fsSL https://raw.githubusercontent.com/bulaya-ute/openplan-api/main/deploy/setup.sh -o setup.sh\n"
    printf "  bash setup.sh\n\n"
    exit 1
fi

# ── Exit trap ─────────────────────────────────────────────────────────────────
_on_exit() {
    local rc=$?
    [[ $rc -ne 0 ]] && err "Setup exited with code $rc. Check $LOG for details."
}
trap _on_exit EXIT

# =============================================================================
# CONFIG DEFAULTS  (overridden by saved config or user input in Stage 2)
# =============================================================================
CFG_API_DIR="/opt/apps/openplan/openplan-api"
CFG_WEB_DIR="/opt/apps/openplan/openplan-web"
CFG_ADMIN_DIR="/opt/apps/openplan/openplan-admin"
CFG_UPDATER_DIR="/opt/apps/openplan/openplan-updater"
CFG_API_SRC="github"
CFG_WEB_SRC="github"
CFG_ADMIN_SRC="github"
CFG_API_PORT="5040"
CFG_UPDATER_PORT="5050"
CFG_API_SVC="openplan-api"
CFG_UPDATER_SVC="openplan-updater"
CFG_DB_HOST="localhost"
CFG_DB_PORT="5432"
CFG_DB_NAME="openplan_db"
CFG_DB_USER="openplan_user"
CFG_DB_PASS=""
CFG_JWT_SECRET=""
CFG_CORS_ORIGINS="http://localhost:5041,http://localhost:5042"
CFG_API_PUBLIC_URL=""
CFG_GITHUB_REPO="bulaya-ute/openplan-api"
CFG_GITHUB_TOKEN=""
CFG_BACKUPS_DIR="/var/backups/openplan"

# =============================================================================
# STAGE 1 — PREFLIGHT
# =============================================================================
stage_preflight() {
    h1 "Stage 1 — Preflight"

    [[ $EUID -eq 0 ]] && ok "Running as root" \
        || warn "Not root — some steps may need sudo or may fail"

    if [[ -f /etc/os-release ]]; then
        # shellcheck source=/dev/null
        . /etc/os-release
        ok "OS: $PRETTY_NAME"
    else
        warn "Cannot detect OS"
    fi

    [[ "$PKG_MGR" == "unknown" ]] \
        && die "No supported package manager found (apt/dnf/yum)."
    ok "Package manager: $PKG_MGR"

    if curl -fsSL --max-time 5 https://github.com >/dev/null 2>&1; then
        ok "Internet: GitHub reachable"
    else
        warn "Cannot reach GitHub — clone-from-GitHub option will not work"
    fi

    # Ensure essential tools exist
    local missing=()
    for tool in git curl openssl; do
        command -v "$tool" &>/dev/null && ok "$tool: found" || missing+=("$tool")
    done
    if [[ ${#missing[@]} -gt 0 ]]; then
        info "Missing tools: ${missing[*]}"
        ask_yn "Install them now?" || die "Required tools are missing."
        [[ "$PKG_MGR" == "apt" ]] && run apt-get update
        _pkg_install "${missing[@]}"
    fi

    stage_done "preflight"
    ok "Preflight complete"
}

# =============================================================================
# STAGE 2 — CONFIGURATION
# =============================================================================

_pick_source() {
    # Ask where to get a component. Sets _VAL to "github" or a local path.
    local name="$1"
    ask_choice "Source for $name" \
        "Clone from GitHub (latest main branch)" \
        "Use an existing local directory"
    if [[ "$_VAL" == "2" ]]; then
        while true; do
            ask "Absolute path to local $name directory" ""
            [[ -d "$_VAL" ]] && return
            err "Directory not found: $_VAL — try again"
        done
    else
        _VAL="github"
    fi
}

_check_svc_conflict() {
    local svc="$1"
    if systemctl is-active --quiet "$svc" 2>/dev/null; then
        warn "Service '$svc' is already running."
        if ask_yn "Stop and disable '$svc' before continuing?"; then
            run systemctl stop "$svc"
            run systemctl disable "$svc" 2>/dev/null || true
            ok "Stopped $svc"
        else
            warn "Leaving $svc running — it will be replaced on next restart."
        fi
    elif systemctl list-unit-files --quiet "${svc}.service" 2>/dev/null; then
        warn "A unit file for '$svc' already exists — it will be overwritten."
    fi
}

stage_configure() {
    h1 "Stage 2 — Configuration"
    info "Press Enter to accept the value shown in [brackets]."

    h2 "Install directories"
    ask "API install path"             "$CFG_API_DIR";     CFG_API_DIR="$_VAL"
    ask "Web app install path"         "$CFG_WEB_DIR";     CFG_WEB_DIR="$_VAL"
    ask "Admin panel install path"     "$CFG_ADMIN_DIR";   CFG_ADMIN_DIR="$_VAL"
    ask "Updater daemon install path"  "$CFG_UPDATER_DIR"; CFG_UPDATER_DIR="$_VAL"

    h2 "Source for each component"
    _pick_source "API";          CFG_API_SRC="$_VAL"
    _pick_source "Web app";      CFG_WEB_SRC="$_VAL"
    _pick_source "Admin panel";  CFG_ADMIN_SRC="$_VAL"

    h2 "Ports"
    ask "API port" "$CFG_API_PORT"; CFG_API_PORT="$_VAL"

    h2 "Service names"
    ask "API systemd service name"     "$CFG_API_SVC";     CFG_API_SVC="$_VAL"
    ask "Updater systemd service name" "$CFG_UPDATER_SVC"; CFG_UPDATER_SVC="$_VAL"

    h2 "Database"
    ask "PostgreSQL host"     "$CFG_DB_HOST"; CFG_DB_HOST="$_VAL"
    ask "PostgreSQL port"     "$CFG_DB_PORT"; CFG_DB_PORT="$_VAL"
    ask "Database name"       "$CFG_DB_NAME"; CFG_DB_NAME="$_VAL"
    ask "Database username"   "$CFG_DB_USER"; CFG_DB_USER="$_VAL"

    local _auto_pass; _auto_pass=$(gen_pass)
    ask_choice "Database password" \
        "Auto-generate  ($_auto_pass)" \
        "Enter manually"
    if [[ "$_VAL" == "2" ]]; then
        ask_secret "Database password"; CFG_DB_PASS="$_VAL"
    else
        CFG_DB_PASS="$_auto_pass"
        ok "Generated DB password (saved to env file)"
    fi

    h2 "JWT Secret"
    ask_choice "JWT signing secret" \
        "Auto-generate" \
        "Enter manually"
    if [[ "$_VAL" == "2" ]]; then
        ask_secret "JWT secret (minimum 32 characters)"; CFG_JWT_SECRET="$_VAL"
    else
        CFG_JWT_SECRET=$(gen_secret)
        ok "JWT secret generated (saved to env file)"
    fi

    h2 "CORS Origins"
    info "Comma-separated URLs the browser uses to reach the web and admin apps."
    info "Example: https://app.example.com,https://admin.example.com"
    ask "CORS origins" "$CFG_CORS_ORIGINS"; CFG_CORS_ORIGINS="$_VAL"

    h2 "Public API URL"
    info "The URL browsers use to reach the API — baked into the web and admin app builds."
    info "Use your public domain in production (e.g. https://api.example.com/api/v1)."
    local _default_api_url="http://localhost:$CFG_API_PORT/api/v1"
    ask "Public API URL" "${CFG_API_PUBLIC_URL:-$_default_api_url}"
    CFG_API_PUBLIC_URL="$_VAL"

    h2 "GitHub (for version management)"
    ask "API GitHub repo (owner/repo)" "$CFG_GITHUB_REPO"; CFG_GITHUB_REPO="$_VAL"
    ask "GitHub token (leave blank for public repos)" "$CFG_GITHUB_TOKEN"
    CFG_GITHUB_TOKEN="$_VAL"

    h2 "Backups"
    ask "Directory for database backup files" "$CFG_BACKUPS_DIR"; CFG_BACKUPS_DIR="$_VAL"

    # Review
    h2 "Review"
    printf "\n"
    printf "  %-34s %s\n" "API directory:"            "$CFG_API_DIR"
    printf "  %-34s %s\n" "Web app directory:"        "$CFG_WEB_DIR"
    printf "  %-34s %s\n" "Admin panel directory:"    "$CFG_ADMIN_DIR"
    printf "  %-34s %s\n" "Updater directory:"        "$CFG_UPDATER_DIR"
    printf "  %-34s %s\n" "API source:"               "$CFG_API_SRC"
    printf "  %-34s %s\n" "Web app source:"           "$CFG_WEB_SRC"
    printf "  %-34s %s\n" "Admin panel source:"       "$CFG_ADMIN_SRC"
    printf "  %-34s %s\n" "API port:"                 "$CFG_API_PORT"
    printf "  %-34s %s\n" "API service name:"         "$CFG_API_SVC"
    printf "  %-34s %s\n" "Updater service name:"     "$CFG_UPDATER_SVC"
    printf "  %-34s %s\n" "Database host:port:"       "$CFG_DB_HOST:$CFG_DB_PORT"
    printf "  %-34s %s\n" "Database name:"            "$CFG_DB_NAME"
    printf "  %-34s %s\n" "Database user:"            "$CFG_DB_USER"
    printf "  %-34s %s\n" "Database password:"        "(hidden)"
    printf "  %-34s %s\n" "CORS origins:"             "$CFG_CORS_ORIGINS"
    printf "  %-34s %s\n" "Public API URL:"           "$CFG_API_PUBLIC_URL"
    printf "  %-34s %s\n" "GitHub repo:"              "$CFG_GITHUB_REPO"
    printf "  %-34s %s\n" "Backups directory:"        "$CFG_BACKUPS_DIR"
    printf "\n"

    ask_yn "Proceed with these settings?" || die "Aborted by user."

    save_conf
    stage_done "configure"
    ok "Configuration saved"
}

# =============================================================================
# STAGE 3 — DATABASE
# =============================================================================
stage_database() {
    h1 "Stage 3 — Database"

    if ! command -v psql &>/dev/null; then
        info "PostgreSQL not found."
        ask_yn "Install PostgreSQL now?" || die "PostgreSQL is required."
        if [[ "$PKG_MGR" == "apt" ]]; then
            run apt-get update
            _pkg_install postgresql postgresql-contrib
        else
            _pkg_install postgresql-server postgresql-contrib
            run postgresql-setup --initdb
        fi
        run systemctl enable --now postgresql
        ok "PostgreSQL installed and started"
    else
        ok "PostgreSQL: $(psql --version)"
    fi

    # Create user
    local _user_exists
    _user_exists=$(sudo -u postgres psql -tAc \
        "SELECT 1 FROM pg_roles WHERE rolname='$CFG_DB_USER'" 2>/dev/null || true)
    if [[ "$_user_exists" == "1" ]]; then
        warn "Database user '$CFG_DB_USER' already exists."
        if ask_yn "Update its password?"; then
            run sudo -u postgres psql \
                -c "ALTER USER $CFG_DB_USER WITH PASSWORD '$CFG_DB_PASS';"
        fi
    else
        run sudo -u postgres psql \
            -c "CREATE USER $CFG_DB_USER WITH PASSWORD '$CFG_DB_PASS';"
        ok "Created database user: $CFG_DB_USER"
    fi

    # Create database
    local _db_exists
    _db_exists=$(sudo -u postgres psql -tAc \
        "SELECT 1 FROM pg_database WHERE datname='$CFG_DB_NAME'" 2>/dev/null || true)
    if [[ "$_db_exists" == "1" ]]; then
        warn "Database '$CFG_DB_NAME' already exists."
        ask_choice "What would you like to do?" \
            "Keep existing data" \
            "Drop and recreate  ⚠  ALL DATA WILL BE LOST"
        if [[ "$_VAL" == "2" ]]; then
            if ask_yn "DESTRUCTIVE: permanently delete all data in '$CFG_DB_NAME'?"; then
                # Terminate active connections before dropping
                run sudo -u postgres psql \
                    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$CFG_DB_NAME' AND pid <> pg_backend_pid();"
                run sudo -u postgres psql -c "DROP DATABASE $CFG_DB_NAME;"
                run sudo -u postgres psql \
                    -c "CREATE DATABASE $CFG_DB_NAME OWNER $CFG_DB_USER;"
                ok "Database dropped and recreated: $CFG_DB_NAME"
            else
                info "Keeping existing database."
            fi
        else
            info "Keeping existing database."
        fi
    else
        run sudo -u postgres psql \
            -c "CREATE DATABASE $CFG_DB_NAME OWNER $CFG_DB_USER;"
        ok "Created database: $CFG_DB_NAME"
    fi

    stage_done "database"
    ok "Database ready"
}

# =============================================================================
# STAGE 4 — API
# =============================================================================

_get_source() {
    local name="$1" src="$2" dest="$3" repo="$4"
    if [[ "$src" == "github" ]]; then
        if [[ -d "$dest/.git" ]]; then
            info "$name already cloned — pulling latest"
            run git -C "$dest" pull
        else
            run mkdir -p "$(dirname "$dest")"
            run git clone "$repo" "$dest"
        fi
    else
        if [[ "$src" == "$dest" ]]; then
            info "Using $name in-place: $dest"
        else
            info "Copying $name from $src → $dest"
            run mkdir -p "$dest"
            run cp -r "$src/." "$dest/"
        fi
    fi
}

stage_api() {
    h1 "Stage 4 — API"

    # .NET 8 SDK
    if ! command -v dotnet &>/dev/null; then
        ask_yn ".NET 8 SDK not found. Install it?" || die ".NET 8 SDK is required."
        if [[ "$PKG_MGR" == "apt" ]]; then
            local _ver_id; _ver_id=$(. /etc/os-release && echo "$VERSION_ID")
            curl -fsSL \
                "https://packages.microsoft.com/config/ubuntu/${_ver_id}/packages-microsoft-prod.deb" \
                -o /tmp/ms-prod.deb
            run dpkg -i /tmp/ms-prod.deb
            run apt-get update
            _pkg_install dotnet-sdk-8.0
        else
            _pkg_install dotnet-sdk-8.0
        fi
        ok ".NET SDK installed: $(dotnet --version)"
    else
        ok ".NET SDK: $(dotnet --version)"
    fi

    # Dedicated system user
    if ! id openplan &>/dev/null 2>&1; then
        run useradd --system --no-create-home --shell /usr/sbin/nologin openplan
        ok "System user 'openplan' created"
    else
        ok "System user 'openplan' already exists"
    fi

    _check_svc_conflict "$CFG_API_SVC"

    _get_source "API" "$CFG_API_SRC" "$CFG_API_DIR" "$API_REPO"

    # Publish
    local _publish_dir="$CFG_API_DIR/publish"
    info "Publishing .NET API (this may take a minute)..."
    run dotnet publish "$CFG_API_DIR/OpenPlan.API" -c Release -o "$_publish_dir"
    run chown -R openplan:openplan "$_publish_dir"
    ok "Published → $_publish_dir"

    # Env file
    run mkdir -p /etc/openplan
    local _env_file="/etc/openplan/api.env"
    if [[ -f "$_env_file" ]]; then
        warn "$_env_file already exists."
        ask_yn "Overwrite it?" || { info "Keeping existing env file."; }
    fi
    if ! $DRY_RUN; then
        cat > "$_env_file" <<EOF
ConnectionStrings__DefaultConnection=Host=$CFG_DB_HOST;Port=$CFG_DB_PORT;Database=$CFG_DB_NAME;Username=$CFG_DB_USER;Password=$CFG_DB_PASS
Jwt__Secret=$CFG_JWT_SECRET
Jwt__Issuer=openplan
Jwt__Audience=openplan-clients
Cors__Origins=$CFG_CORS_ORIGINS
GitHub__ApiRepo=$CFG_GITHUB_REPO
GitHub__Token=$CFG_GITHUB_TOKEN
Backups__Directory=$CFG_BACKUPS_DIR
Updater__Url=http://127.0.0.1:$CFG_UPDATER_PORT/update
EOF
        chown root:openplan "$_env_file"
        chmod 640 "$_env_file"
        ok "Env file: $_env_file"
    fi

    # Backups directory
    run mkdir -p "$CFG_BACKUPS_DIR"
    run chown openplan:openplan "$CFG_BACKUPS_DIR"

    # Apply migrations explicitly before the service first starts.
    # The service also calls MigrateAsync() on startup, but running it here
    # with the correct credentials prevents a crash if the DB state is stale.
    h2 "Applying database migrations"
    export PATH="$PATH:$HOME/.dotnet/tools"
    if ! dotnet tool list -g 2>/dev/null | grep -q 'dotnet-ef'; then
        info "Installing dotnet-ef tool..."
        run dotnet tool install --global dotnet-ef
    else
        ok "dotnet-ef: found"
    fi
    if ! $DRY_RUN; then
        dotnet ef database update \
            --project "$CFG_API_DIR/OpenPlan.API" \
            --connection "Host=$CFG_DB_HOST;Port=$CFG_DB_PORT;Database=$CFG_DB_NAME;Username=$CFG_DB_USER;Password=$CFG_DB_PASS" \
            && ok "Migrations applied" \
            || warn "dotnet ef migration failed — the service will retry on startup; check $LOG"
    fi

    # Systemd service
    if ! $DRY_RUN; then
        cat > "/etc/systemd/system/$CFG_API_SVC.service" <<EOF
[Unit]
Description=OpenPlan API
Documentation=https://github.com/bulaya-ute/openplan-api
After=network.target postgresql.service
Wants=postgresql.service

[Service]
Type=simple
User=openplan
Group=openplan
WorkingDirectory=$_publish_dir
ExecStart=/usr/bin/dotnet $_publish_dir/OpenPlan.API.dll
Environment=ASPNETCORE_URLS=http://localhost:$CFG_API_PORT
Environment=ASPNETCORE_ENVIRONMENT=Production
EnvironmentFile=$_env_file
Restart=always
RestartSec=10
NoNewPrivileges=true
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF
        ok "Service file: /etc/systemd/system/$CFG_API_SVC.service"
    fi

    run systemctl daemon-reload
    run systemctl enable "$CFG_API_SVC"
    run systemctl start "$CFG_API_SVC"
    ok "Service '$CFG_API_SVC' enabled and started"

    stage_done "api"
    ok "API setup complete"
}

# =============================================================================
# STAGE 5 — WEB APPS
# =============================================================================
stage_webapps() {
    h1 "Stage 5 — Web Apps"

    if ! command -v node &>/dev/null; then
        ask_yn "Node.js not found. Install Node.js 20 via NodeSource?" \
            || die "Node.js is required to build the web apps."
        if [[ "$PKG_MGR" == "apt" ]]; then
            curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
            _pkg_install nodejs
        else
            curl -fsSL https://rpm.nodesource.com/setup_20.x | bash -
            _pkg_install nodejs
        fi
        ok "Node.js installed: $(node --version)"
    else
        ok "Node.js: $(node --version)"
    fi

    local _api_url="${CFG_API_PUBLIC_URL:-http://localhost:$CFG_API_PORT/api/v1}"
    info "Building with VITE_API_URL=$_api_url"

    _build_static() {
        local name="$1" src="$2" dest="$3" repo="$4"
        h2 "$name"
        _get_source "$name" "$src" "$dest" "$repo"
        info "Installing npm dependencies..."
        run npm --prefix "$dest" ci
        info "Building..."
        if $DRY_RUN; then
            warn "[dry-run] VITE_API_URL=$_api_url npm --prefix $dest run build"
        else
            VITE_API_URL="$_api_url" npm --prefix "$dest" run build
            log "RUN: VITE_API_URL=$_api_url npm --prefix $dest run build"
        fi
        ok "$name built → $dest/dist"
    }

    _build_static "Web App"      "$CFG_WEB_SRC"   "$CFG_WEB_DIR"   "$WEB_REPO"
    _build_static "Admin Panel"  "$CFG_ADMIN_SRC" "$CFG_ADMIN_DIR" "$ADMIN_REPO"

    stage_done "webapps"
    ok "Web apps built"
}

# =============================================================================
# STAGE 6 — UPDATER DAEMON
# =============================================================================
stage_updater() {
    h1 "Stage 6 — Updater Daemon"

    if ! command -v python3 &>/dev/null; then
        ask_yn "Python 3 not found. Install it?" || die "Python 3 is required."
        _pkg_install python3
        ok "Python 3 installed: $(python3 --version)"
    else
        ok "Python 3: $(python3 --version)"
    fi

    _check_svc_conflict "$CFG_UPDATER_SVC"
    run mkdir -p "$CFG_UPDATER_DIR"

    # Write the daemon (single-quoted heredoc — no variable expansion inside)
    if ! $DRY_RUN; then
        cat > "$CFG_UPDATER_DIR/updater.py" <<'PYEOF'
#!/usr/bin/env python3
"""
OpenPlan Updater Daemon
Listens on localhost:UPDATER_PORT only (never exposed to the internet).
Receives POST /update from the API when an admin triggers a version switch.
"""
import http.server, json, subprocess, threading, logging, os, sys

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s %(levelname)s %(message)s',
    handlers=[logging.StreamHandler(sys.stdout)]
)
log = logging.getLogger(__name__)

PORT        = int(os.environ.get('UPDATER_PORT', '5050'))
API_DIR     = os.environ.get('API_DIR',    '/opt/apps/openplan/openplan-api')
WEB_DIR     = os.environ.get('WEB_DIR',    '/opt/apps/openplan/openplan-web')
ADMIN_DIR   = os.environ.get('ADMIN_DIR',  '/opt/apps/openplan/openplan-admin')
API_SVC     = os.environ.get('API_SVC',    'openplan-api')
PUBLISH_DIR = os.path.join(API_DIR, 'publish')

def sh(cmd, cwd=None):
    log.info('$ %s', cmd if isinstance(cmd, str) else ' '.join(cmd))
    subprocess.check_call(cmd, cwd=cwd, shell=isinstance(cmd, str))

def switch_api(version):
    log.info('Switching API → v%s', version)
    sh(['git', 'fetch', '--tags'], cwd=API_DIR)
    sh(['git', '-c', 'advice.detachedHead=false', 'checkout', f'tags/v{version}'], cwd=API_DIR)
    sh(['dotnet', 'publish', 'OpenPlan.API', '-c', 'Release', '-o', PUBLISH_DIR], cwd=API_DIR)
    sh(['chown', '-R', 'openplan:openplan', PUBLISH_DIR])
    sh(['systemctl', 'restart', API_SVC])
    log.info('API now at v%s', version)

def switch_static(version, component):
    target = WEB_DIR if component == 'web' else ADMIN_DIR
    log.info('Switching %s → v%s', component, version)
    sh(['git', 'fetch', '--tags'], cwd=target)
    sh(['git', '-c', 'advice.detachedHead=false', 'checkout', f'tags/v{version}'], cwd=target)
    sh(['npm', 'ci'], cwd=target)
    sh(['npm', 'run', 'build'], cwd=target)
    log.info('%s now at v%s', component, version)

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, fmt, *args): log.info(fmt, *args)

    def send_json(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code)
        self.send_header('Content-Type', 'application/json')
        self.send_header('Content-Length', len(data))
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        if self.path != '/update':
            return self.send_json(404, {'error': 'not found'})
        try:
            n         = int(self.headers.get('Content-Length', 0))
            body      = json.loads(self.rfile.read(n))
            component = body.get('component', '')
            version   = body.get('version', '')
            if not component or not version:
                return self.send_json(400, {'error': 'component and version required'})
            if component not in ('api', 'web', 'admin'):
                return self.send_json(400, {'error': 'invalid component'})
            # Respond immediately; do the work in the background
            self.send_json(202, {'status': 'accepted', 'component': component, 'version': version})
            def _work():
                try:
                    if component == 'api': switch_api(version)
                    else:                  switch_static(version, component)
                except Exception as exc:
                    log.error('Update failed: %s', exc)
            threading.Thread(target=_work, daemon=True).start()
        except Exception as exc:
            log.error('Request error: %s', exc)
            self.send_json(500, {'error': str(exc)})

if __name__ == '__main__':
    server = http.server.HTTPServer(('127.0.0.1', PORT), Handler)
    log.info('Updater daemon listening on 127.0.0.1:%d', PORT)
    server.serve_forever()
PYEOF
        chmod +x "$CFG_UPDATER_DIR/updater.py"
        ok "Daemon script: $CFG_UPDATER_DIR/updater.py"
    fi

    # Systemd service (double-quoted heredoc — CFG_* variables expand here)
    if ! $DRY_RUN; then
        cat > "/etc/systemd/system/$CFG_UPDATER_SVC.service" <<EOF
[Unit]
Description=OpenPlan Updater Daemon
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$CFG_UPDATER_DIR
ExecStart=/usr/bin/python3 $CFG_UPDATER_DIR/updater.py
Environment=UPDATER_PORT=$CFG_UPDATER_PORT
Environment=API_DIR=$CFG_API_DIR
Environment=WEB_DIR=$CFG_WEB_DIR
Environment=ADMIN_DIR=$CFG_ADMIN_DIR
Environment=API_SVC=$CFG_API_SVC
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
        ok "Service: /etc/systemd/system/$CFG_UPDATER_SVC.service"
    fi

    run systemctl daemon-reload
    run systemctl enable "$CFG_UPDATER_SVC"
    run systemctl start "$CFG_UPDATER_SVC"
    ok "Updater daemon enabled and started"

    stage_done "updater"
    ok "Updater daemon setup complete"
}

# =============================================================================
# STAGE 7 — SUMMARY
# =============================================================================
stage_summary() {
    h1 "Setup Complete"

    printf "${GREEN}${BOLD}"
    printf "  %-32s %s\n" "API:"            "http://localhost:$CFG_API_PORT/api/v1"
    printf "  %-32s %s\n" "Web build:"      "$CFG_WEB_DIR/dist"
    printf "  %-32s %s\n" "Admin build:"    "$CFG_ADMIN_DIR/dist"
    printf "  %-32s %s\n" "Updater:"        "http://127.0.0.1:$CFG_UPDATER_PORT  (internal only)"
    printf "${NC}\n"

    printf "${BOLD}  Useful commands:${NC}\n"
    printf "  systemctl status|start|stop|restart %s\n" "$CFG_API_SVC"
    printf "  journalctl -u %s -f\n\n" "$CFG_API_SVC"

    printf "${YELLOW}${BOLD}  Remaining manual steps:${NC}\n"
    printf "  1. Point your reverse proxy (Caddy/Nginx) at:\n"
    printf "       API:    http://localhost:%s   (proxy_pass)\n" "$CFG_API_PORT"
    printf "       Web:    %s/dist              (file_server / root)\n" "$CFG_WEB_DIR"
    printf "       Admin:  %s/dist              (file_server / root)\n" "$CFG_ADMIN_DIR"
    printf "  2. Register the first account — it is automatically granted admin privileges\n\n"
    printf "  Full log: %s\n\n" "$LOG"
}

# =============================================================================
# MAIN
# =============================================================================
main() {
    : > "$LOG"
    [[ -f "$STATE" ]] || touch "$STATE"

    printf "\n${BOLD}${BLUE}"
    printf "  ╔══════════════════════════════════════════╗\n"
    printf "  ║   OpenPlan  ·  Linux Setup  ·  v%-8s ║\n" "$SETUP_VERSION"
    printf "  ╚══════════════════════════════════════════╝\n"
    printf "${NC}\n"

    $DRY_RUN && warn "DRY-RUN MODE — no changes will be made\n"

    # Load config saved from a previous (possibly partial) run
    load_conf

    stage_ran "preflight"  || stage_preflight
    stage_ran "configure"  || stage_configure
    load_conf  # reload after configure in case it just ran
    stage_ran "database"   || stage_database
    stage_ran "api"        || stage_api
    stage_ran "webapps"    || stage_webapps
    stage_ran "updater"    || stage_updater
    stage_summary
}

main "$@"
