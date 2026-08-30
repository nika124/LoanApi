#!/usr/bin/env bash
set -euo pipefail

api_base="${LOAN_API_BASE_URL:-http://localhost:5102}"
project="src/LoanApi.Api/LoanApi.Api.csproj"
smoke_dir="$(mktemp -d)"
trap 'rm -rf "$smoke_dir"' EXIT

secret_lines="$(dotnet user-secrets list --project "$project")"
accountant_username="$(printf '%s\n' "$secret_lines" | sed -n 's/^SeedAccountant:Username = //p')"
accountant_password="$(printf '%s\n' "$secret_lines" | sed -n 's/^SeedAccountant:Password = //p')"
unset secret_lines

if [[ -z "$accountant_username" || -z "$accountant_password" ]]; then
  echo "Development Accountant User Secrets are required." >&2
  exit 1
fi

suffix="$(date +%s)"
first_username="smoke.first.$suffix"
second_username="smoke.second.$suffix"
smoke_password="$(openssl rand -hex 12)aA1!"

request() {
  local method="$1"
  local path="$2"
  local token="$3"
  local payload="$4"
  local output="$5"
  local args=(-sS -o "$output" -w '%{http_code}' -X "$method")

  if [[ -n "$token" ]]; then
    args+=(-H "Authorization: Bearer $token")
  fi
  if [[ -n "$payload" ]]; then
    args+=(-H 'Content-Type: application/json' --data "$payload")
  fi

  curl "${args[@]}" "$api_base$path"
}

expect() {
  local expected="$1"
  local actual="$2"
  local label="$3"
  if [[ "$actual" != "$expected" ]]; then
    echo "$label failed: expected HTTP $expected, received $actual" >&2
    exit 1
  fi
  echo "$label: HTTP $actual"
}

swagger_status="$(request GET '/swagger/index.html' '' '' "$smoke_dir/swagger.html")"
expect 200 "$swagger_status" 'Swagger UI'
openapi_status="$(request GET '/swagger/v1/swagger.json' '' '' "$smoke_dir/openapi.json")"
expect 200 "$openapi_status" 'OpenAPI document'
jq -e '.components.securitySchemes.bearer' "$smoke_dir/openapi.json" >/dev/null

first_registration="$(jq -nc \
  --arg username "$first_username" \
  --arg email "$first_username@example.com" \
  --arg password "$smoke_password" \
  '{firstName:"Smoke",lastName:"First",username:$username,email:$email,age:30,monthlyIncome:5000,password:$password}')"
second_registration="$(jq -nc \
  --arg username "$second_username" \
  --arg email "$second_username@example.com" \
  --arg password "$smoke_password" \
  '{firstName:"Smoke",lastName:"Second",username:$username,email:$email,age:31,monthlyIncome:4500,password:$password}')"

register_status="$(request POST '/api/auth/users/register' '' "$first_registration" "$smoke_dir/first-user.json")"
expect 201 "$register_status" 'First User registration'
first_user_id="$(jq -r '.id' "$smoke_dir/first-user.json")"
duplicate_status="$(request POST '/api/auth/users/register' '' "$first_registration" "$smoke_dir/duplicate.json")"
expect 409 "$duplicate_status" 'Duplicate registration'
register_second_status="$(request POST '/api/auth/users/register' '' "$second_registration" "$smoke_dir/second-user.json")"
expect 201 "$register_second_status" 'Second User registration'

first_login="$(jq -nc --arg username "$first_username" --arg password "$smoke_password" \
  '{usernameOrEmail:$username,password:$password}')"
second_login="$(jq -nc --arg username "$second_username" --arg password "$smoke_password" \
  '{usernameOrEmail:$username,password:$password}')"
login_status="$(request POST '/api/auth/users/login' '' "$first_login" "$smoke_dir/first-login.json")"
expect 200 "$login_status" 'First User login'
first_token="$(jq -r '.accessToken' "$smoke_dir/first-login.json")"
second_login_status="$(request POST '/api/auth/users/login' '' "$second_login" "$smoke_dir/second-login.json")"
expect 200 "$second_login_status" 'Second User login'
second_token="$(jq -r '.accessToken' "$smoke_dir/second-login.json")"
bad_login_payload="$(jq -nc --arg username "$first_username" '{usernameOrEmail:$username,password:"wrong"}')"
bad_login_status="$(request POST '/api/auth/users/login' '' "$bad_login_payload" "$smoke_dir/bad-login.json")"
expect 401 "$bad_login_status" 'Bad credentials'

loan_payload='{"loanType":"FastLoan","amount":2500,"currency":"gel","periodMonths":12,"status":"Approved"}'
unauthorized_status="$(request POST '/api/loans' '' "$loan_payload" "$smoke_dir/unauthorized.json")"
expect 401 "$unauthorized_status" 'Unauthenticated loan write'
create_status="$(request POST '/api/loans' "$first_token" "$loan_payload" "$smoke_dir/loan.json")"
expect 201 "$create_status" 'Pending loan creation'
loan_id="$(jq -r '.id' "$smoke_dir/loan.json")"
jq -e '.status == "Pending"' "$smoke_dir/loan.json" >/dev/null

own_read_status="$(request GET "/api/loans/$loan_id" "$first_token" '' "$smoke_dir/own-read.json")"
expect 200 "$own_read_status" 'Owner loan read'
isolated_read_status="$(request GET "/api/loans/$loan_id" "$second_token" '' "$smoke_dir/isolated-read.json")"
expect 403 "$isolated_read_status" 'Other User isolation'

owner_update='{"loanType":"Installment","amount":2750,"currency":"usd","periodMonths":18,"status":"Approved"}'
owner_update_status="$(request PUT "/api/loans/$loan_id" "$first_token" "$owner_update" "$smoke_dir/owner-update.json")"
expect 200 "$owner_update_status" 'Pending owner update'
jq -e '.status == "Pending"' "$smoke_dir/owner-update.json" >/dev/null

accountant_login="$(jq -nc --arg username "$accountant_username" --arg password "$accountant_password" \
  '{usernameOrEmail:$username,password:$password}')"
accountant_login_status="$(request POST '/api/auth/accountants/login' '' "$accountant_login" "$smoke_dir/accountant-login.json")"
expect 200 "$accountant_login_status" 'Accountant login'
accountant_token="$(jq -r '.accessToken' "$smoke_dir/accountant-login.json")"

approve_status="$(request PATCH "/api/loans/$loan_id" "$accountant_token" '{"status":"Approved"}' "$smoke_dir/approved.json")"
expect 200 "$approve_status" 'Accountant approval'
processed_update_status="$(request PUT "/api/loans/$loan_id" "$first_token" "$owner_update" "$smoke_dir/processed-update.json")"
expect 409 "$processed_update_status" 'Processed User update rejection'
accountant_update_status="$(request PATCH "/api/loans/$loan_id" "$accountant_token" '{"amount":3000,"status":"Rejected"}' "$smoke_dir/accountant-update.json")"
expect 200 "$accountant_update_status" 'Accountant processed-loan update'

if blocked_until="$(date -u -v+5S '+%Y-%m-%dT%H:%M:%SZ' 2>/dev/null)"; then
  :
else
  blocked_until="$(date -u -d '+5 seconds' '+%Y-%m-%dT%H:%M:%SZ')"
fi
block_payload="$(jq -nc --arg until "$blocked_until" '{blockedUntilUtc:$until,reason:"Smoke-test block"}')"
block_status="$(request POST "/api/users/$first_user_id/blocks" "$accountant_token" "$block_payload" "$smoke_dir/block.json")"
expect 204 "$block_status" 'Accountant timed block'
blocked_create_status="$(request POST '/api/loans' "$first_token" "$loan_payload" "$smoke_dir/blocked-create.json")"
expect 403 "$blocked_create_status" 'Active block enforcement'

sleep 6
expired_create_status="$(request POST '/api/loans' "$first_token" "$loan_payload" "$smoke_dir/expired-create.json")"
expect 201 "$expired_create_status" 'Expired block release'
second_loan_id="$(jq -r '.id' "$smoke_dir/expired-create.json")"

accountant_delete_status="$(request DELETE "/api/loans/$loan_id" "$accountant_token" '' "$smoke_dir/accountant-delete.json")"
expect 204 "$accountant_delete_status" 'Accountant processed-loan delete'
deleted_read_status="$(request GET "/api/loans/$loan_id" "$accountant_token" '' "$smoke_dir/deleted-read.json")"
expect 404 "$deleted_read_status" 'Deleted loan exclusion'
history_status="$(request GET "/api/loans/$loan_id/history" "$accountant_token" '' "$smoke_dir/history.json")"
expect 200 "$history_status" 'Deleted loan history'
jq -e 'map(.action) | index("Created") and index("Updated") and index("StatusChanged") and index("Deleted")' \
  "$smoke_dir/history.json" >/dev/null

owner_delete_status="$(request DELETE "/api/loans/$second_loan_id" "$first_token" '' "$smoke_dir/owner-delete.json")"
expect 204 "$owner_delete_status" 'Owner Pending-loan delete'

unset accountant_password smoke_password first_token second_token accountant_token
echo "Smoke test passed for User $first_user_id and Loan $loan_id."
