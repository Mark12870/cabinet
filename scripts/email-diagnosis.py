#!/usr/bin/env python3

import os
import smtplib
import ssl
import sys
from email.message import EmailMessage
from pathlib import Path

from markdown_it import MarkdownIt


def formatted(diagnosis: str) -> str:
    readable = diagnosis.replace("<details><summary>As recorded</summary>", "### As recorded").replace("</details>", "")
    rendered = MarkdownIt("commonmark", {"html": False}).enable("table").disable("image").render(readable)
    styles = {
        "h2": "font-size:24px;margin:0 0 20px;",
        "h3": "font-size:18px;margin:28px 0 12px;",
        "p": "margin:12px 0;",
        "ul": "padding-left:24px;",
        "li": "margin:8px 0;",
        "blockquote": "margin:16px 0;padding:0 16px;border-left:4px solid #8fa58c;",
        "pre": "padding:16px;background:#e8d6bd;border-radius:8px;white-space:pre-wrap;overflow-wrap:anywhere;",
        "code": "font-family:monospace;font-size:13px;",
        "table": "border-collapse:collapse;width:100%;",
        "th": "padding:8px;text-align:left;border:1px solid #e8d6bd;",
        "td": "padding:8px;border:1px solid #e8d6bd;",
    }
    for tag, style in styles.items():
        rendered = rendered.replace(f"<{tag}>", f'<{tag} style="{style}">')
    rendered = rendered.replace('<a ', '<a style="color:#227d66;" ')
    return ('<!doctype html><html><body style="margin:0;background:#fff8eb;color:#211814;">'
            '<div style="max-width:720px;margin:auto;padding:24px;font-family:Arial,sans-serif;font-size:16px;line-height:1.6;">'
            + rendered + '</div></body></html>')


def main() -> int:
    required = ("SMTP_HOST", "SMTP_USERNAME", "SMTP_PASSWORD", "SMTP_TO")
    missing = [name for name in required if not os.environ.get(name)]
    if missing:
        print("::error::Configure these GitHub Actions secrets for diagnosis email: " + ", ".join(missing))
        return 1

    try:
        port = int(os.environ.get("SMTP_PORT") or "465")
        message = EmailMessage()
        message["From"] = os.environ["SMTP_USERNAME"]
        message["To"] = os.environ["SMTP_TO"]
        message["Subject"] = f"Cabinet Plugins AI diagnosis — run {os.environ['GITHUB_RUN_ID']}"
        diagnosis = Path(sys.argv[1]).read_text(encoding="utf-8")
        message.set_content(diagnosis)
        message.add_alternative(formatted(diagnosis), subtype="html")

        context = ssl.create_default_context()
        host = os.environ["SMTP_HOST"]
        if port == 465:
            connection = smtplib.SMTP_SSL(host, port, timeout=30, context=context)
        else:
            connection = smtplib.SMTP(host, port, timeout=30)
        with connection as smtp:
            if port != 465:
                smtp.starttls(context=context)
                smtp.ehlo()
            smtp.login(os.environ["SMTP_USERNAME"], os.environ["SMTP_PASSWORD"])
            smtp.send_message(message)
    except (OSError, smtplib.SMTPException, ValueError, KeyError) as error:
        print(f"::error::Diagnosis email failed ({type(error).__name__}). The AI summary is on the Actions run page.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
