# Security Policy

## 🔒 Reporting Security Vulnerabilities

We take the security of Daily Bread seriously. If you discover a security vulnerability, please help us protect our users by reporting it responsibly.

### How to Report

**Please DO NOT report security vulnerabilities through public GitHub issues.**

Instead, please report them via one of the following methods:

1. **GitHub Security Advisories** (Preferred): Use the [Security tab](https://github.com/jshauns81/Daily_Bread/security/advisories) to privately report a vulnerability
2. **Email**: Send details to the repository owner (see GitHub profile for contact information)

### What to Include

When reporting a vulnerability, please include:

- A description of the vulnerability
- Steps to reproduce the issue
- Potential impact of the vulnerability
- Any suggested fixes or mitigations (if applicable)

### Response Timeline

- We aim to acknowledge receipt of your vulnerability report within 48 hours
- We will provide a more detailed response within 7 days, including next steps
- We will keep you informed of the progress toward fixing the vulnerability

## 🛡️ Security Best Practices

### For Contributors and Users

When working with this application, please follow these security guidelines:

#### Never Commit Sensitive Data

**NEVER commit these to version control:**
- Database passwords or connection strings
- API keys, tokens, or secrets
- Private keys or certificates (`.key`, `.pem`, `.pfx`)
- SSH keys
- User credentials
- Real email addresses or personal information in seed data

#### Use Safe Configuration

**DO:**
- ✅ Use `appsettings.json.example` with placeholder values
- ✅ Copy to `appsettings.json` locally and add real credentials
- ✅ Use environment variables for production deployments
- ✅ Use `.env` files for local development (already in `.gitignore`)
- ✅ Use secure secret management services (Azure Key Vault, AWS Secrets Manager, etc.)
- ✅ Use strong, unique passwords
- ✅ Regularly rotate credentials

**DON'T:**
- ❌ Commit `appsettings.json` with real credentials
- ❌ Hardcode secrets in source code
- ❌ Share production credentials in development environments
- ❌ Commit `.env` files to version control
- ❌ Use default or weak passwords

#### Database Security

- Use strong passwords for database users
- Restrict database access to specific IP addresses when possible
- Never expose the database directly to the internet
- Regularly backup your database
- Use SSL/TLS for database connections in production
- Follow the principle of least privilege for database users

#### Application Security

- Keep dependencies up to date
- Review and address Dependabot security alerts
- Enable GitHub secret scanning
- Use HTTPS in production
- Configure proper CORS policies
- Implement rate limiting for API endpoints
- Validate and sanitize all user inputs
- Use parameterized queries (Entity Framework Core handles this)

### Secure Deployment Checklist

Before deploying to production:

- [ ] All secrets are stored in environment variables or secret management service
- [ ] `appsettings.json` with real credentials is NOT in version control
- [ ] Database is not accessible from the public internet
- [ ] SSL/TLS certificates are properly configured
- [ ] Authentication and authorization are properly configured
- [ ] All dependencies are up to date
- [ ] Security headers are configured (HSTS, CSP, X-Frame-Options, etc.)
- [ ] Logging is configured but doesn't log sensitive data
- [ ] Error messages don't expose sensitive system information
- [ ] Database backups are automated and tested

## 🚨 Known Security Considerations

### Previous Security Issues

**Database Credentials Exposure (Fixed):**
- Previous versions of this repository had exposed database credentials in `appsettings.json`
- The password `M4xd0g01!!` was publicly committed
- **Action Required**: If you deployed before this fix, change your database password immediately

### Post-Deployment Actions Required

If you deployed Daily Bread before the security fix was merged:

1. **Immediately change the database password** from `M4xd0g01!!`
2. Verify the database is not accessible from the internet or restrict access
3. Review database logs for any unauthorized access
4. Rotate any other credentials that may have been exposed
5. Create local `appsettings.json` from the example template
6. Ensure `.env` file has secure passwords

### Ongoing Security

- Monitor the repository's [Security tab](https://github.com/jshauns81/Daily_Bread/security) for alerts
- Subscribe to Dependabot alerts
- Review pull requests for security implications
- Keep production systems patched and updated

## 📝 Security Update Policy

We will:
- Address critical security vulnerabilities as quickly as possible
- Publish security advisories for confirmed vulnerabilities
- Credit reporters (if desired) in security advisories
- Backport security fixes to supported versions when applicable

## 🔗 Additional Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [ASP.NET Core Security Best Practices](https://learn.microsoft.com/en-us/aspnet/core/security/)
- [GitHub Security Best Practices](https://docs.github.com/en/code-security)
- [Entity Framework Core Security](https://learn.microsoft.com/en-us/ef/core/miscellaneous/security)

## 📜 License

This security policy is part of the Daily Bread project and is covered under the same license terms.

---

**Thank you for helping keep Daily Bread and its users safe!**
