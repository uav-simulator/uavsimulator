#!/usr/bin/env python3
"""Build a concise PDF assignment document for internship submission."""

from __future__ import annotations

from pathlib import Path

from reportlab.lib.colors import HexColor
from reportlab.lib.enums import TA_CENTER, TA_JUSTIFY, TA_LEFT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.platypus import ListFlowable, ListItem, Paragraph, SimpleDocTemplate, Spacer


ROOT = Path(__file__).resolve().parents[3]
OUTPUT_PDF = ROOT / "docs/report/practice/practice_assignment_2.pdf"


def register_fonts() -> tuple[str, str]:
    candidates = [
        (
            "/System/Library/Fonts/Supplemental/Arial.ttf",
            "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        ),
        (
            "/Library/Fonts/Arial.ttf",
            "/Library/Fonts/Arial Bold.ttf",
        ),
        (
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        ),
    ]
    for regular, bold in candidates:
        regular_path = Path(regular)
        bold_path = Path(bold)
        if regular_path.exists() and bold_path.exists():
            pdfmetrics.registerFont(TTFont("PracticeRegular", str(regular_path)))
            pdfmetrics.registerFont(TTFont("PracticeBold", str(bold_path)))
            return "PracticeRegular", "PracticeBold"
    raise FileNotFoundError("No suitable TTF font found for PDF generation.")


REGULAR_FONT, BOLD_FONT = register_fonts()


TITLE = "Задание на учебную практику"
TOPIC = (
    "Разработка единого WebUI-драйвера и web-системы управления "
    "роботизированной платформой Keyestudio KS0223 для реального стенда "
    "и Unity-симулятора."
)

TASKS = [
    "проанализировать штатное программное обеспечение платформы KS0223, каналы управления, передачи видеоданных и получения сенсорной информации;",
    "разработать серверную часть web-системы, обеспечивающую работу с реальным стендом и Unity-симулятором;",
    "реализовать единый web-интерфейс оператора для подключения, управления движением, просмотра видеоданных и диагностики;",
    "интегрировать канал сенсоров и исполнительных узлов платформы;",
    "обеспечить работу Unity-симулятора через тот же интерфейс управления;",
    "провести проверку работоспособности системы и подготовить отчетные материалы по результатам практики.",
]

EXPECTED_RESULTS = [
    "работоспособный WebUI-драйвер для платформы KS0223;",
    "единый программный контур управления для реального стенда и Unity-симулятора;",
    "подтвержденные сценарии подключения, управления, получения видеоданных и сенсорной информации;",
    "отчет и сопутствующие материалы по результатам практики.",
]


def build_styles():
    styles = getSampleStyleSheet()
    styles.add(
        ParagraphStyle(
            name="PracticeTitle",
            parent=styles["Title"],
            fontName=BOLD_FONT,
            fontSize=15,
            leading=18,
            alignment=TA_CENTER,
            textColor=HexColor("#111827"),
            spaceAfter=8,
        )
    )
    styles.add(
        ParagraphStyle(
            name="PracticeMeta",
            parent=styles["Normal"],
            fontName=REGULAR_FONT,
            fontSize=11,
            leading=15,
            alignment=TA_LEFT,
            textColor=HexColor("#111827"),
            spaceAfter=2,
        )
    )
    styles.add(
        ParagraphStyle(
            name="PracticeBody",
            parent=styles["BodyText"],
            fontName=BOLD_FONT,
            fontSize=11,
            leading=15,
            alignment=TA_JUSTIFY,
            textColor=HexColor("#111827"),
            spaceAfter=4,
        )
    )
    styles.add(
        ParagraphStyle(
            name="PracticeBullet",
            parent=styles["BodyText"],
            fontName=REGULAR_FONT,
            fontSize=11,
            leading=15,
            alignment=TA_JUSTIFY,
            textColor=HexColor("#111827"),
            spaceAfter=4,
        )
    )
    return styles


def make_numbered_list(items: list[str], style) -> ListFlowable:
    return ListFlowable(
        [ListItem(Paragraph(item, style), leftIndent=0, value="-") for item in items],
        bulletType="bullet",
        start="-",
        leftIndent=16,
    )


def main() -> None:
    styles = build_styles()
    doc = SimpleDocTemplate(
        str(OUTPUT_PDF),
        pagesize=A4,
        leftMargin=25 * mm,
        rightMargin=20 * mm,
        topMargin=20 * mm,
        bottomMargin=20 * mm,
        title="Задание на учебную практику",
        author="Н.М. Горовенко",
    )

    story = [
        Paragraph(TITLE, styles["PracticeTitle"]),
        Spacer(1, 3 * mm),
        Paragraph(
            "В качестве индивидуального задания на учебную практику необходимо выполнить работу по теме: "
            + TOPIC,
            styles["PracticeBody"],
        ),
        Paragraph("Для выполнения задания необходимо:", styles["PracticeBody"]),
        make_numbered_list(TASKS, styles["PracticeBullet"]),
        Spacer(1, 3 * mm),
        Paragraph("Планируемый результат выполнения задания:", styles["PracticeBody"]),
        make_numbered_list(EXPECTED_RESULTS, styles["PracticeBullet"]),
    ]

    doc.build(story)


if __name__ == "__main__":
    main()
