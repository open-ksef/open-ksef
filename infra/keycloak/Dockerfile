FROM quay.io/keycloak/keycloak:26.0

# Realm is baked in – no local file mount needed.
# APP_EXTERNAL_BASE_URL defaults to http://localhost:8080 (wizard updates it via Keycloak Admin API).
# Google IdP client credentials are left empty (wizard configures them if needed).
COPY realm-openksef.json /opt/keycloak/data/import/realm-openksef.json
