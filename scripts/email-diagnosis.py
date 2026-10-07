#!/usr/bin/env python3

import os
import smtplib
import ssl
import sys
from email.message import EmailMessage
from pathlib import Path


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
        message.set_content(Path(sys.argv[1]).read_text(encoding="utf-8"))

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
