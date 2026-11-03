# Security Policy

## Supported Versions

| Version | Supported          |
|---------|--------------------|
| latest  | Yes                |

## Reporting a Vulnerability

**Please do NOT open a public GitHub issue for security vulnerabilities.**

If you discover a security vulnerability in OpenKSeF, please report it responsibly:

1. **Email:** Send a description to **openksef@proton.me**
2. **Include:**
   - A description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

We will acknowledge your report within **48 hours** and aim to provide a fix or mitigation within **7 days** for critical issues.

## Scope

This policy applies to the OpenKSeF codebase and its official Docker images. It does not cover:

- Third-party dependencies (report to their respective maintainers)
- The KSeF test environment operated by the Polish Ministry of Finance
- Self-hosted instances with custom configurations

## Security Best Practices for Deployers

- **Never** use default passwords (`openksef_dev_password`, `admin`) in production
- Set a strong `ENCRYPTION_KEY` (generate with `openssl rand -base64 32`) and back it up outside the database; `ENCRYPTION_KEY_FILE` works with your own compose file, the shipped one does not forward it. On an existing installation do not just set or replace it: follow the upgrade procedure in [docs/admin-setup.md](docs/admin-setup.md#upgrade-from-v007-or-earlier)
- Once the key is adopted, database dumps do not contain it. Confirm with `SELECT count(*) FROM "SystemConfigs" WHERE "Key"='encryption_key'`, which must return 0. Dumps taken earlier still hold it; see [docs/admin-setup.md](docs/admin-setup.md)
- Use HTTPS in production (configure via reverse proxy)
- Restrict Keycloak admin access to trusted networks
- Rotate KSeF API tokens periodically
- Keep Docker images and dependencies up to date

## Acknowledgments

We appreciate the security research community and will credit reporters (with permission) in release notes.
