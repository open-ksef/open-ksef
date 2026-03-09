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
- Set a strong `ENCRYPTION_KEY` (generate with `openssl rand -base64 32`)
- Use HTTPS in production (configure via reverse proxy)
- Restrict Keycloak admin access to trusted networks
- Rotate KSeF API tokens periodically
- Keep Docker images and dependencies up to date

## Acknowledgments

We appreciate the security research community and will credit reporters (with permission) in release notes.
