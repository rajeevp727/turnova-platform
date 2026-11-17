#!/usr/bin/env bash
set -euo pipefail

event_name="${1:?event name is required}"
event_path="${2:?GitHub event JSON path is required}"
phase_state_path="${3:?checked-in phase state path is required}"

if [[ ! -f "$event_path" || ! -f "$phase_state_path" ]]; then
  echo "Event payload or TurnOva phase state file is missing." >&2
  exit 2
fi

api_state="$(jq -er '.api | select(type == "string")' "$phase_state_path")"
android_state="$(jq -er '.android | select(type == "string")' "$phase_state_path")"
ios_state="$(jq -er '.ios | select(type == "string")' "$phase_state_path")"

case "$api_state" in in_progress|complete) ;; *) echo "Invalid API phase state." >&2; exit 2 ;; esac
case "$android_state" in not_started|approved|in_progress|complete) ;; *) echo "Invalid Android phase state." >&2; exit 2 ;; esac
case "$ios_state" in not_started|approved|in_progress|complete) ;; *) echo "Invalid iOS phase state." >&2; exit 2 ;; esac

has_label() {
  jq -e --arg label "$1" '.pull_request.labels // [] | any(.[]; .name == $label)' "$event_path" >/dev/null
}

android_phase=false
ios_phase=false

if [[ "$event_name" == "push" ]]; then
  if [[ "$api_state" == "complete" && "$android_state" != "not_started" ]]; then
    android_phase=true
  fi
  if [[ "$api_state" == "complete" && "$android_state" == "complete" && "$ios_state" != "not_started" ]]; then
    ios_phase=true
  fi
elif [[ "$event_name" == "pull_request" ]]; then
  if [[ "$api_state" == "complete" && "$android_state" != "not_started" ]] &&
     has_label "turnova:api-phase-complete" &&
     { has_label "turnova:android-phase-approved" || has_label "turnova:android-phase-complete"; }; then
    android_phase=true
  fi
  if [[ "$api_state" == "complete" && "$android_state" == "complete" && "$ios_state" != "not_started" ]] &&
     has_label "turnova:api-phase-complete" &&
     has_label "turnova:android-phase-complete" &&
     has_label "turnova:ios-phase-approved"; then
    ios_phase=true
  fi
fi

printf 'android_phase=%s\nios_phase=%s\n' "$android_phase" "$ios_phase"
