FROM quay.io/keycloak/keycloak:26.8.2@sha256:c68e286cacd87b942ac1939a0a1481461109d6acd3a57dc496969a319a76bbf0
COPY themes/openksef /opt/keycloak/themes/openksef
COPY --from=portal src/index.css src/components/ui.css src/components/auth.css /opt/keycloak/themes/openksef/login/resources/css/
COPY --from=portal src/assets/fonts /opt/keycloak/themes/openksef/login/resources/css/assets/fonts
COPY --from=portal public/assets/LICENSE-Inter.txt public/assets/LICENSE-JetBrainsMono.txt /opt/keycloak/themes/openksef/login/resources/css/assets/fonts/
COPY --from=portal public/favicon.svg public/favicon-32.png public/favicon-180.png public/icons.svg /opt/keycloak/themes/openksef/login/resources/img/
