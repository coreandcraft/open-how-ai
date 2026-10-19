import { t } from "../../shared/i18n/i18n";

export function GitHubSetup() {
  return (
    <details className="github-setup">
      <summary>{t("GitHub App setup and operations")}</summary>
      <p>
        {t(
          "An administrator configures the App once. Repository owners then install it on selected public repositories.",
        )}
      </p>
      <ol>
        <li>
          <a
            href="https://github.com/settings/apps/new"
            target="_blank"
            rel="noopener noreferrer"
          >
            {t("Register a GitHub App")}
          </a>
          .{" "}
          {t(
            "Use your openhow URL as Homepage URL. Allow installation on Any account if other users will connect repositories.",
          )}
        </li>
        <li>
          {t(
            "Grant repository Contents: Read-only; Metadata: Read-only is included. Keep other permissions off.",
          )}
        </li>
        <li>
          {t(
            "Leave Callback URL and Setup URL blank. Disable user authorization, device flow, and webhooks for this integration.",
          )}
        </li>
        <li>
          {t(
            "Generate a private key and record the Client ID and App slug. Keep the PEM key on the server only.",
          )}
        </li>
        <li>
          {t("Configure these server settings, then restart:")}
          <pre>
            {
              "GitHub__Enabled=true\nGitHub__AuthenticationMode=App\nGitHub__App__ClientId=<your Client ID>\nGitHub__App__Slug=<your App slug>"
            }
          </pre>
          {t(
            "Store GitHub:App:PrivateKeyPem in local user-secrets, or GitHub--App--PrivateKeyPem in the application's Azure Key Vault.",
          )}
        </li>
        <li>
          {t(
            "Check App credentials below, install it on selected repositories, then return here and link a repository.",
          )}
        </li>
      </ol>
      <p>
        {t(
          "The server needs outbound HTTPS to api.github.com and an accurate clock. Installation tokens renew automatically; no webhook or callback service is required.",
        )}
      </p>
      <p>
        {t(
          "To rotate a key, generate a replacement, update the server secret, restart and check credentials, then delete the old key in GitHub. To disable the feature, set GitHub__Enabled=false and restart.",
        )}
      </p>
    </details>
  );
}
