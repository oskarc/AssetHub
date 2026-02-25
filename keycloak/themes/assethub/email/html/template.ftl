<#macro emailLayout>
<!DOCTYPE html>
<html lang="${locale!'en'}">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>${msg("emailTitle")}</title>
    <style>
        body {
            margin: 0;
            padding: 0;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background-color: #f5f5f5;
            line-height: 1.6;
        }
        .email-container {
            max-width: 600px;
            margin: 0 auto;
            background-color: #ffffff;
            border-radius: 8px;
            overflow: hidden;
            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
        }
        .email-header {
            background: linear-gradient(135deg, #7c3aed 0%, #a855f7 100%);
            padding: 32px 24px;
            text-align: center;
        }
        .email-header h1 {
            color: #ffffff;
            margin: 0;
            font-size: 28px;
            font-weight: 600;
        }
        .email-body {
            padding: 32px 24px;
        }
        .email-body h2 {
            color: #1f2937;
            font-size: 22px;
            margin-top: 0;
            margin-bottom: 16px;
        }
        .email-body p {
            color: #4b5563;
            margin: 0 0 16px 0;
        }
        .button {
            display: inline-block;
            background: linear-gradient(135deg, #7c3aed 0%, #a855f7 100%);
            color: #ffffff !important;
            text-decoration: none;
            padding: 14px 28px;
            border-radius: 6px;
            font-weight: 600;
            font-size: 16px;
            margin: 16px 0;
        }
        .button:hover {
            opacity: 0.9;
        }
        .code-box {
            background-color: #f3f4f6;
            border-radius: 8px;
            padding: 16px;
            margin: 16px 0;
            text-align: center;
        }
        .code-box .code {
            font-size: 32px;
            font-weight: bold;
            letter-spacing: 4px;
            color: #7c3aed;
        }
        .info-text {
            color: #6b7280;
            font-size: 14px;
        }
        .warning-text {
            color: #d97706;
            font-size: 14px;
        }
        .email-footer {
            background-color: #f9fafb;
            padding: 24px;
            text-align: center;
            border-top: 1px solid #e5e7eb;
        }
        .email-footer p {
            color: #9ca3af;
            font-size: 12px;
            margin: 0;
        }
        .link-fallback {
            background-color: #f3f4f6;
            border-radius: 6px;
            padding: 12px;
            margin: 16px 0;
            word-break: break-all;
            font-size: 12px;
            color: #6b7280;
        }
    </style>
</head>
<body>
    <div style="padding: 24px;">
        <div class="email-container">
            <div class="email-header">
                <h1>AssetHub</h1>
            </div>
            <div class="email-body">
                <#nested>
            </div>
            <div class="email-footer">
                <p>${msg("emailFooter", realmName)}</p>
            </div>
        </div>
    </div>
</body>
</html>
</#macro>
