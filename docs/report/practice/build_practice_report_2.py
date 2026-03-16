#!/usr/bin/env python3
"""
Rebuild and normalize practice_report_2.docx in-place.

The script keeps the existing title page, tables, listings, and embedded media,
but updates:
- main section heading alignment and formatting;
- architecture and scenario sections;
- chapter about stock KS0223 software limitations;
- source numbering and list formatting;
- figure numbering and captions;
- insertion of new diagrams rendered with Pillow.
"""

from __future__ import annotations

from pathlib import Path
import re
from typing import Iterable

from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.shared import Cm, Pt
from docx.text.paragraph import Paragraph


ROOT = Path(__file__).resolve().parents[3]
REPORT_PATH = ROOT / "docs/report/practice/practice_report_2.docx"
ASSETS_DIR = ROOT / "docs/report/practice/assets"

SYSTEM_OVERVIEW_PNG = ASSETS_DIR / "system_overview_diagram.png"
SYSTEM_INTERACTION_PNG = ASSETS_DIR / "system_interaction_diagram.png"
USE_CASE_HUMAN_REAL_PNG = ASSETS_DIR / "use_case_human_real_robot.png"
USE_CASE_HUMAN_SIM_PNG = ASSETS_DIR / "use_case_human_unity_sim.png"
USE_CASE_AI_SIM_PNG = ASSETS_DIR / "use_case_ai_unity_training.png"
USE_CASE_AI_TRANSFER_PNG = ASSETS_DIR / "use_case_ai_sim_to_real.png"


MAIN_HEADINGS = {
    "ЦЕЛЬ И ЗАДАЧИ ПРАКТИКИ",
    "МЕСТО ПРАКТИКИ В ОБЩЕМ ПРОЕКТЕ",
    "ИСХОДНЫЕ УСЛОВИЯ И АНАЛИЗ",
    "ПРОДУКТОВАЯ АРХИТЕКТУРА",
    "РЕАЛИЗАЦИЯ КОНТУРА REAL-ROBOT ДЛЯ KS0223",
    "ЕДИНЫЙ API ОПЕРАТОРА",
    "ИНТЕГРАЦИЯ С UNITY-СИМУЛЯТОРОМ КАК ЦИФРОВЫМ ДВОЙНИКОМ",
    "СЦЕНАРИИ ИСПОЛЬЗОВАНИЯ И ДАЛЬНЕЙШЕГО РАЗВИТИЯ",
    "ОГРАНИЧЕНИЯ ШТАТНОГО ПО KS0223 И ВЫПОЛНЕННЫЕ ДОРАБОТКИ",
    "РЕЗУЛЬТАТЫ ПРАКТИКИ",
}

STRUCTURAL_HEADINGS = {
    "ВВЕДЕНИЕ",
    "ЗАКЛЮЧЕНИЕ",
    "СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ",
}


ARCHITECTURE_TEXT = [
    "Архитектура решения построена вокруг единого Web UI и единого backend-сервиса на ASP.NET Core. Frontend отвечает за интерфейс оператора, backend нормализует статусы, команды, камеру, телеметрию и журналирование, а различия между physical runtime и Unity runtime локализованы внутри runtime providers.",
    "В backend зарегистрированы два провайдера исполнения: `RealKs0223RuntimeProvider` и `UnityKs0223RuntimeProvider`. Сервис `RuntimeControlService` выбирает активный режим, переключает жизненный цикл подключения и предоставляет frontend одинаковый набор endpoint для `connect`, `disconnect`, `status`, `health`, `command`, `camera`, `sensors` и `logs`.",
    "Общая продуктовая схема показана на рисунке 1, а типовая последовательность подключения и отправки команд - на рисунке 2. Более детальная обзорная диаграмма состава системы приведена на рисунке 3: человек работает через Web UI, AI-модель может использовать тот же операторский контур через backend, а backend подключается либо к physical runtime KS0223, либо к Unity runtime.",
    "Диаграмма взаимодействия систем на рисунке 4 показывает, что Web UI обменивается с backend командами, статусом, health, камерой, телеметрией и логами, после чего backend транслирует запросы в конкретный runtime. Для `real-robot` используются TCP `5051`, camera ingest и sensor bridge, а для `unity-sim` - runtime catalog, runtime selection, reset и step-loop поверх HTTP API симулятора.",
    "Важная особенность Unity-контура состоит в том, что backend работает не с абстрактным симулятором целиком, а с активной симуляцией и выбранным агентом. Это позволяет адресно выбирать `agentId`, `camera mode`, track и vehicle configuration, сохраняя единый операторский UX и подготавливая систему к multi-agent сценариям и будущему sim-to-real маршруту.",
]

SCENARIO_TEXT = [
    "Первый слой использования системы относится к человеку-оператору. В этом случае Web UI выступает как единый инженерный пульт: пользователь выбирает режим `real-robot` или `unity-sim`, подключается к нужной среде исполнения, управляет движением, камерой, сенсорами и контролирует диагностический статус. Базовые сценарии ручной работы с реальным стендом и с симулятором показаны на рисунках 8 и 9.",
    "Для реального стенда оператор подключается к Raspberry Pi, получает видеопоток и телеметрию из backend-слоя, а затем управляет исполнительными узлами KS0223 в едином интерфейсе. Для симулятора тот же интерфейс позволяет выбрать runtime, track, машинку, активного агента и режим камеры, не меняя frontend и не переключаясь на отдельные инструменты.",
    "Второй слой относится к AI и исследовательским сценариям. На рисунках 10 и 11 разделены два use-case: работа модели управления внутри Unity и последующий маршрут `sim -> real`, при котором та же модель сначала проверяется в цифровом двойнике, а затем подключается к физическому KS0223 через тот же operator contour.",
    "Именно такая схема важна для общей магистерской работы: одна и та же система может использоваться сначала для ручной отладки и валидации поведения в Unity, затем для подключения модели, а затем для проверки этой модели на физическом стенде с теми же status, health, camera и logging точками наблюдения.",
    "Таким образом, практика фиксирует прикладной операторский слой, а магистерская работа развивает сам симулятор, сценарии экспериментов и будущий sim-to-real перенос моделей управления. Разделение на product-core и research-layer позволяет расширять Python SDK, ROS2 bridge и Jupyter notebooks без усложнения базового пользовательского пути.",
]

LIMITATIONS_TEXT = [
    "Реальный стенд KS0223 использует штатное программное обеспечение на Raspberry Pi. В базовый комплект входят `MainControl.py`, принимающий команды управления по TCP `5051`, `FramesSend.py`, отвечающий за передачу кадров, и набор сетевых и systemd-служб, обеспечивающих запуск и сетевую доступность платформы.",
    "В процессе практики было установлено, что штатный контур предоставляет только часть необходимых функций. TCP-канал `5051` принимает строковые команды движения и камеры, но не возвращает телеметрию; часть операторских данных недоступна по сети; видеопоток живёт отдельным каналом и требует собственного процесса отправки кадров.",
    "Дополнительно были выявлены эксплуатационные ограничения штатного ПО. После подключения машинки к Wi-Fi и в ряде сетевых сценариев доступ к платформе становился нестабильным; после reboot мог пропадать канал управления; запуск `MainControl.py`, `FramesSend.py` и сетевых служб происходил непредсказуемо; часть нужных функций вообще отсутствовала из коробки и должна была быть добавлена поверх стандартной конфигурации.",
    "Для получения рабочего operator flow поверх штатного ПО были реализованы отдельные доработки: `sensor bridge` для телеметрии и периферии, camera ingest на стороне backend, JSONL-журналирование операторских сессий, эксплуатационные скрипты развёртывания и обновления, а также Docker-контур и healthcheck для стандартного локального запуска web-комплекса.",
    "Отдельным частным, но важным случаем стал runtime fix для сети и автозапуска. Исправление отключает конфликтный legacy-профиль `eth0`, оставляет единым сетевым менеджером `dhcpcd`, обновляет автозапуск `ks0223-maincontrol.service` и `ks0223-framesend.service`, после чего после контрольного reboot подтверждается стабильная доступность `5051`, восстановление видеоканала и предсказуемый запуск управляющих сервисов.",
]


def load_font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf" if bold else "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial Bold.ttf" if bold else "/Library/Fonts/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Times New Roman Bold.ttf"
        if bold
        else "/System/Library/Fonts/Supplemental/Times New Roman.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ]
    for candidate in candidates:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size=size)
    return ImageFont.load_default()


FONT_REGULAR = load_font(30, bold=False)
FONT_SMALL = load_font(24, bold=False)
FONT_BOLD = load_font(32, bold=True)
FONT_TITLE = load_font(38, bold=True)


def text_size(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.ImageFont) -> tuple[int, int]:
    left, top, right, bottom = draw.multiline_textbbox((0, 0), text, font=font, spacing=6, align="center")
    return right - left, bottom - top


def draw_centered_text(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    text: str,
    *,
    font: ImageFont.ImageFont,
    fill: str = "#1f2937",
) -> None:
    x1, y1, x2, y2 = box
    width, height = text_size(draw, text, font)
    draw.multiline_text(
        ((x1 + x2 - width) / 2, (y1 + y2 - height) / 2),
        text,
        font=font,
        fill=fill,
        spacing=6,
        align="center",
    )


def rounded_box(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    text: str,
    *,
    fill: str,
    outline: str = "#111827",
    radius: int = 24,
    font: ImageFont.ImageFont = FONT_REGULAR,
    text_fill: str = "#111827",
) -> None:
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=4)
    draw_centered_text(draw, box, text, font=font, fill=text_fill)


def ellipse_box(
    draw: ImageDraw.ImageDraw,
    box: tuple[int, int, int, int],
    text: str,
    *,
    fill: str = "#ffffff",
    outline: str = "#111827",
    font: ImageFont.ImageFont = FONT_SMALL,
) -> None:
    draw.ellipse(box, fill=fill, outline=outline, width=4)
    draw_centered_text(draw, box, text, font=font)


def arrow(
    draw: ImageDraw.ImageDraw,
    start: tuple[int, int],
    end: tuple[int, int],
    *,
    fill: str = "#334155",
    width: int = 5,
    head: int = 18,
) -> None:
    x1, y1 = start
    x2, y2 = end
    draw.line((x1, y1, x2, y2), fill=fill, width=width)
    if abs(x2 - x1) >= abs(y2 - y1):
        direction = 1 if x2 >= x1 else -1
        points = [
            (x2, y2),
            (x2 - direction * head, y2 - head // 2),
            (x2 - direction * head, y2 + head // 2),
        ]
    else:
        direction = 1 if y2 >= y1 else -1
        points = [
            (x2, y2),
            (x2 - head // 2, y2 - direction * head),
            (x2 + head // 2, y2 - direction * head),
        ]
    draw.polygon(points, fill=fill)


def place_edge_label(
    draw: ImageDraw.ImageDraw,
    center: tuple[int, int],
    text: str,
    *,
    font: ImageFont.ImageFont = FONT_SMALL,
    fill: str = "#0f172a",
    background: str = "#ffffff",
) -> None:
    width, height = text_size(draw, text, font)
    x, y = center
    pad = 10
    box = (x - width // 2 - pad, y - height // 2 - pad, x + width // 2 + pad, y + height // 2 + pad)
    draw.rounded_rectangle(box, radius=12, fill=background, outline="#cbd5e1", width=2)
    draw_centered_text(draw, box, text, font=font, fill=fill)


def draw_actor(draw: ImageDraw.ImageDraw, x: int, y: int, label: str) -> None:
    draw.ellipse((x - 26, y, x + 26, y + 52), outline="#111827", width=4, fill="#ffffff")
    draw.line((x, y + 52, x, y + 135), fill="#111827", width=4)
    draw.line((x - 42, y + 82, x + 42, y + 82), fill="#111827", width=4)
    draw.line((x, y + 135, x - 40, y + 190), fill="#111827", width=4)
    draw.line((x, y + 135, x + 40, y + 190), fill="#111827", width=4)
    draw.text((x - 80, y + 205), label, font=FONT_SMALL, fill="#111827")


def generate_system_overview() -> None:
    image = Image.new("RGB", (2200, 1450), "#f8fafc")
    draw = ImageDraw.Draw(image)

    draw.text((70, 50), "Обзорная диаграмма системы", font=FONT_TITLE, fill="#0f172a")

    human = (120, 250, 430, 390)
    ai_model = (120, 610, 430, 750)
    web_ui = (640, 250, 980, 390)
    backend = (1120, 250, 1530, 390)
    unity = (1630, 180, 2090, 930)
    simulation = (1705, 365, 2015, 505)
    agents = (1705, 640, 2015, 800)
    real_robot = (1120, 1040, 1530, 1180)

    rounded_box(draw, human, "Человек /\nоператор", fill="#dbeafe")
    rounded_box(draw, ai_model, "AI /\nмодель управления", fill="#fde68a")
    rounded_box(draw, web_ui, "Web UI", fill="#bfdbfe")
    rounded_box(draw, backend, "Operator Backend", fill="#c7d2fe")

    draw.rounded_rectangle(unity, radius=28, fill="#dcfce7", outline="#166534", width=5)
    draw.text((1680, 205), "Unity Simulator", font=FONT_BOLD, fill="#14532d")
    rounded_box(draw, simulation, "Active\nSimulation", fill="#ecfccb", outline="#166534")
    rounded_box(draw, agents, "Vehicles /\nAgents", fill="#f0fdf4", outline="#166534")
    rounded_box(draw, real_robot, "Real KS0223\n(physical runtime)", fill="#fecaca")

    arrow(draw, (430, 320), (640, 320))
    arrow(draw, (980, 320), (1120, 320))
    arrow(draw, (1530, 320), (1630, 320))
    arrow(draw, (1425, 390), (1425, 1040))
    arrow(draw, (1860, 505), (1860, 640))
    arrow(draw, (430, 680), (1120, 680))

    place_edge_label(draw, (535, 280), "ручное управление")
    place_edge_label(draw, (1050, 280), "единый API")
    place_edge_label(draw, (1580, 280), "unity-sim")
    place_edge_label(draw, (1465, 725), "real-robot")
    place_edge_label(draw, (1860, 575), "выбор agentId")
    place_edge_label(draw, (770, 640), "backend / operator contour")

    notes_box = (640, 1040, 980, 1235)
    rounded_box(
        draw,
        notes_box,
        "Один backend нормализует\nstatus, health, camera,\ntelemetry и logs для\nобоих runtime-режимов.",
        fill="#ffffff",
        outline="#94a3b8",
        font=FONT_SMALL,
    )

    image.save(SYSTEM_OVERVIEW_PNG)


def generate_system_interaction() -> None:
    image = Image.new("RGB", (2200, 1520), "#ffffff")
    draw = ImageDraw.Draw(image)

    draw.text((70, 50), "Диаграмма взаимодействия систем", font=FONT_TITLE, fill="#0f172a")

    ui = (140, 240, 470, 400)
    backend = (740, 240, 1160, 400)
    real_robot = (1450, 170, 2040, 470)
    unity = (1450, 690, 2040, 1330)
    sim = (1540, 930, 1940, 1060)
    agent = (1540, 1140, 1940, 1270)

    rounded_box(draw, ui, "Web UI", fill="#dbeafe")
    rounded_box(draw, backend, "Operator Backend", fill="#c7d2fe")
    draw.rounded_rectangle(real_robot, radius=26, fill="#fee2e2", outline="#7f1d1d", width=5)
    draw.text((1520, 200), "Real KS0223", font=FONT_BOLD, fill="#7f1d1d")
    draw.text((1510, 270), "TCP 5051\nSensor bridge\nCamera ingest", font=FONT_REGULAR, fill="#111827", spacing=10)

    draw.rounded_rectangle(unity, radius=26, fill="#dcfce7", outline="#166534", width=5)
    draw.text((1605, 725), "Unity Simulator", font=FONT_BOLD, fill="#14532d")
    rounded_box(draw, sim, "Active\nSimulation", fill="#ecfccb", outline="#166534")
    rounded_box(draw, agent, "Selected\nAgent", fill="#f0fdf4", outline="#166534")

    arrow(draw, (470, 320), (740, 320))
    arrow(draw, (740, 350), (470, 350))
    arrow(draw, (1160, 300), (1450, 300))
    arrow(draw, (1450, 340), (1160, 340))
    arrow(draw, (1160, 1020), (1450, 1020))
    arrow(draw, (1450, 1060), (1160, 1060))
    arrow(draw, (1740, 1060), (1740, 1140))

    place_edge_label(draw, (605, 265), "connect / disconnect")
    place_edge_label(draw, (605, 405), "status / health /\ncommand / camera /\ntelemetry / logs")
    place_edge_label(draw, (1305, 250), "TCP-команды /\ncamera / sensors")
    place_edge_label(draw, (1305, 410), "status / telemetry /\nframes / errors")
    place_edge_label(draw, (1305, 950), "runtime catalog /\nruntime selection /\nreset / step")
    place_edge_label(draw, (1305, 1135), "normalized status /\nagent-scoped camera /\ntelemetry")
    place_edge_label(draw, (1740, 1100), "agentId / camera mode")

    notes = (140, 520, 1160, 1020)
    draw.rounded_rectangle(notes, radius=24, fill="#f8fafc", outline="#94a3b8", width=4)
    draw.text((180, 560), "Нормализация backend-слоя", font=FONT_BOLD, fill="#0f172a")
    bullet_lines = [
        "• Web UI не знает, какой runtime сейчас активен.",
        "• Backend скрывает transport-детали Pi TCP и Unity step-loop.",
        "• Для Unity backend адресует команды и камеру конкретному agentId.",
        "• Для real-robot backend собирает данные из отдельных каналов управления, камеры и sensor bridge.",
        "• Один и тот же API используется для ручного управления, логирования и будущих AI-сценариев.",
    ]
    y = 630
    for line in bullet_lines:
        draw.text((190, y), line, font=FONT_SMALL, fill="#111827")
        y += 78

    image.save(SYSTEM_INTERACTION_PNG)


def generate_use_case_diagram(path: Path, actor_label: str, use_cases: Iterable[str], title: str) -> None:
    image = Image.new("RGB", (2200, 1500), "#ffffff")
    draw = ImageDraw.Draw(image)

    draw.text((70, 50), title, font=FONT_TITLE, fill="#0f172a")
    draw_actor(draw, 250, 420, actor_label)

    boundary = (540, 180, 2060, 1370)
    draw.rounded_rectangle(boundary, radius=28, outline="#1f2937", width=4, fill="#f8fafc")
    draw.text((590, 210), "Единый WebUI-драйвер / operator contour", font=FONT_BOLD, fill="#111827")

    centers = [
        (880, 360),
        (1660, 360),
        (880, 580),
        (1660, 580),
        (880, 800),
        (1660, 800),
        (880, 1020),
        (1660, 1020),
        (1270, 1230),
    ]
    boxes = []
    for center, text in zip(centers, use_cases):
        cx, cy = center
        box = (cx - 290, cy - 70, cx + 290, cy + 70)
        boxes.append(box)
        ellipse_box(draw, box, text)
        arrow(draw, (320, 505), (box[0], cy), fill="#64748b", width=3, head=14)

    image.save(path)


def generate_diagrams() -> None:
    ASSETS_DIR.mkdir(parents=True, exist_ok=True)
    generate_system_overview()
    generate_system_interaction()
    generate_use_case_diagram(
        USE_CASE_HUMAN_REAL_PNG,
        "Оператор /\nинженер",
        [
            "Выбрать режим\nreal-robot",
            "Подключиться к\nреальной машинке",
            "Наблюдать камеру\nи телеметрию",
            "Управлять\nдвижением",
            "Управлять сенсорами\nи исполнительными\nузлами",
            "Просматривать логи\nи диагностику",
            "Выполнить аварийную\nостановку",
        ],
        "Use-case диаграмма: человек и реальный стенд",
    )
    generate_use_case_diagram(
        USE_CASE_HUMAN_SIM_PNG,
        "Оператор /\nинженер",
        [
            "Выбрать режим\nunity-sim",
            "Подключиться к\nсимулятору",
            "Выбрать track,\nмашинку и\nактивного агента",
            "Выбрать\ncamera mode",
            "Управлять\nдвижением",
            "Наблюдать камеру\nи телеметрию",
            "Просматривать логи\nи диагностику",
        ],
        "Use-case диаграмма: человек и Unity-симулятор",
    )
    generate_use_case_diagram(
        USE_CASE_AI_SIM_PNG,
        "AI /\nмодель\nуправления",
        [
            "Подключиться к\nагенту в Unity",
            "Получать состояние,\nкадр и телеметрию",
            "Управлять машинкой\nв симуляции",
            "Выбрать сценарий,\ntrack и agentId",
            "Сохранять логи\nи метрики",
            "Сравнивать rollout\nмежду агентами",
        ],
        "Use-case диаграмма: AI внутри Unity-симулятора",
    )
    generate_use_case_diagram(
        USE_CASE_AI_TRANSFER_PNG,
        "AI /\nмодель\nуправления",
        [
            "Подготовить перенос\nsim -> real",
            "Подключиться к\nреальному KS0223",
            "Получать status,\nhealth и камеру",
            "Наблюдать поведение\nмодели на стенде",
            "Сравнивать\nunity-sim и\nreal-robot",
            "Анализировать логи\nи диагностические\nданные",
        ],
        "Use-case диаграмма: AI и маршрут sim-to-real",
    )


def iter_paragraphs(doc: Document) -> list[Paragraph]:
    return list(doc.paragraphs)


def paragraph_index(doc: Document, target: Paragraph) -> int:
    for index, paragraph in enumerate(doc.paragraphs):
        if paragraph._element is target._element:
            return index
    raise ValueError(f"Paragraph not found in document: {target.text!r}")


def find_paragraph(doc: Document, text: str) -> Paragraph:
    for paragraph in doc.paragraphs:
        if paragraph.text.strip() == text:
            return paragraph
    raise ValueError(f"Paragraph not found: {text}")


def find_paragraph_contains(doc: Document, fragment: str) -> Paragraph | None:
    for paragraph in doc.paragraphs:
        if fragment in paragraph.text:
            return paragraph
    return None


def delete_paragraph(paragraph: Paragraph) -> None:
    paragraph._element.getparent().remove(paragraph._element)


def insert_paragraph_after(paragraph: Paragraph, text: str = "", style: str | None = None) -> Paragraph:
    new_p = OxmlElement("w:p")
    paragraph._element.addnext(new_p)
    new_paragraph = Paragraph(new_p, paragraph._parent)
    if style:
        new_paragraph.style = style
    if text:
        new_paragraph.add_run(text)
    return new_paragraph


def insert_picture_after(paragraph: Paragraph, image_path: Path, width_cm: float, caption: str) -> Paragraph:
    image_paragraph = insert_paragraph_after(paragraph, style="Normal")
    image_paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    image_paragraph.paragraph_format.first_line_indent = Pt(0)
    image_paragraph.paragraph_format.left_indent = Pt(0)
    run = image_paragraph.add_run()
    run.add_picture(str(image_path), width=Cm(width_cm))

    caption_paragraph = insert_paragraph_after(image_paragraph, caption, style="Рисунок")
    caption_paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    caption_paragraph.paragraph_format.first_line_indent = Pt(0)
    caption_paragraph.paragraph_format.left_indent = Pt(0)
    return caption_paragraph


def replace_paragraph_text(paragraph: Paragraph, text: str) -> None:
    paragraph.clear()
    paragraph.add_run(text)


def apply_main_heading_style(paragraph: Paragraph) -> None:
    paragraph.style = "Heading 1"
    paragraph.alignment = None
    paragraph.paragraph_format.first_line_indent = None
    paragraph.paragraph_format.left_indent = None
    paragraph.paragraph_format.right_indent = None
    paragraph.paragraph_format.space_before = None
    paragraph.paragraph_format.space_after = None
    paragraph.paragraph_format.line_spacing = None


def apply_structural_heading_style(paragraph: Paragraph) -> None:
    paragraph.style = "Heading 3"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.first_line_indent = None
    paragraph.paragraph_format.left_indent = None
    paragraph.paragraph_format.right_indent = None
    paragraph.paragraph_format.space_before = None
    paragraph.paragraph_format.space_after = None
    paragraph.paragraph_format.line_spacing = None


def apply_normal_style(paragraph: Paragraph) -> None:
    paragraph.style = "Normal"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    paragraph.paragraph_format.first_line_indent = None
    paragraph.paragraph_format.left_indent = None
    paragraph.paragraph_format.right_indent = None
    paragraph.paragraph_format.space_before = None
    paragraph.paragraph_format.space_after = None
    paragraph.paragraph_format.line_spacing = None


def apply_list_style(paragraph: Paragraph) -> None:
    paragraph.style = "List Paragraph"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
    paragraph.paragraph_format.first_line_indent = None
    paragraph.paragraph_format.left_indent = None
    paragraph.paragraph_format.right_indent = None
    paragraph.paragraph_format.space_before = None
    paragraph.paragraph_format.space_after = None
    paragraph.paragraph_format.line_spacing = None


def apply_figure_caption_style(paragraph: Paragraph) -> None:
    paragraph.style = "Рисунок"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
    paragraph.paragraph_format.first_line_indent = Pt(0)
    paragraph.paragraph_format.left_indent = Pt(0)
    paragraph.paragraph_format.right_indent = Pt(0)


def apply_listing_title_style(paragraph: Paragraph) -> None:
    paragraph.style = "Normal"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.LEFT
    paragraph.paragraph_format.first_line_indent = Pt(0)
    paragraph.paragraph_format.left_indent = Pt(0)
    paragraph.paragraph_format.right_indent = Pt(0)
    paragraph.paragraph_format.space_before = Pt(6)
    paragraph.paragraph_format.space_after = Pt(0)
    paragraph.paragraph_format.line_spacing = 1.0


def apply_code_block_style(paragraph: Paragraph) -> None:
    paragraph.style = "Листинг"
    paragraph.alignment = WD_ALIGN_PARAGRAPH.LEFT
    paragraph.paragraph_format.first_line_indent = Pt(0)
    paragraph.paragraph_format.line_spacing = 1.0


def ensure_section_text(doc: Document, heading_text: str, new_paragraphs: list[str]) -> None:
    heading = find_paragraph(doc, heading_text)
    paragraphs = iter_paragraphs(doc)
    start = paragraph_index(doc, heading) + 1
    end = len(paragraphs)
    for idx in range(start, len(paragraphs)):
        if paragraphs[idx].style.name in {"Heading 1", "Heading 3"}:
            end = idx
            break

    body_paragraphs = paragraphs[start:end]
    non_empty = [
        paragraph for paragraph in body_paragraphs if paragraph.style.name == "Normal" and paragraph.text.strip()
    ]

    for paragraph, text in zip(non_empty, new_paragraphs):
        replace_paragraph_text(paragraph, text)
        apply_normal_style(paragraph)

    if len(non_empty) > len(new_paragraphs):
        for paragraph in reversed(non_empty[len(new_paragraphs) :]):
            delete_paragraph(paragraph)
    elif len(non_empty) < len(new_paragraphs):
        anchor = non_empty[-1] if non_empty else heading
        for text in new_paragraphs[len(non_empty) :]:
            anchor = insert_paragraph_after(anchor, text, style="Normal")
            apply_normal_style(anchor)


def normalize_paragraphs(doc: Document) -> None:
    intro_index = paragraph_index(doc, find_paragraph(doc, "ВВЕДЕНИЕ"))
    for paragraph in doc.paragraphs:
        text = paragraph.text.strip()
        if not text:
            continue
        if paragraph_index(doc, paragraph) < intro_index:
            continue
        if text in MAIN_HEADINGS:
            apply_main_heading_style(paragraph)
            continue
        if text in STRUCTURAL_HEADINGS or text.startswith("ПРИЛОЖЕНИЕ "):
            apply_structural_heading_style(paragraph)
            continue
        if text.startswith("Рисунок "):
            apply_figure_caption_style(paragraph)
            continue
        if text.startswith("Листинг "):
            apply_listing_title_style(paragraph)
            continue
        if text.startswith("Таблица "):
            paragraph.style = "Normal"
            paragraph.alignment = WD_ALIGN_PARAGRAPH.LEFT
            paragraph.paragraph_format.first_line_indent = Pt(0)
            paragraph.paragraph_format.left_indent = Pt(0)
            paragraph.paragraph_format.right_indent = Pt(0)
            continue
        if paragraph.style.name == "Листинг":
            apply_code_block_style(paragraph)
            continue
        if paragraph.style.name == "List Paragraph":
            apply_list_style(paragraph)
            continue
        apply_normal_style(paragraph)


def renumber_results(doc: Document) -> None:
    heading = find_paragraph(doc, "РЕЗУЛЬТАТЫ ПРАКТИКИ")
    paragraphs = iter_paragraphs(doc)
    start = paragraph_index(doc, heading) + 1
    numbered = []
    for paragraph in paragraphs[start:]:
        if paragraph.style.name in {"Heading 1", "Heading 3"}:
            break
        if paragraph.style.name == "List Paragraph" and paragraph.text.strip():
            numbered.append(paragraph)

    for index, paragraph in enumerate(numbered, start=1):
        raw = re.sub(r"^(?:\d+\.\s*)+", "", paragraph.text.strip())
        replace_paragraph_text(paragraph, f"{index}. {raw}")
        apply_list_style(paragraph)


def renumber_sources(doc: Document) -> None:
    heading = find_paragraph(doc, "СПИСОК ИСПОЛЬЗОВАННЫХ ИСТОЧНИКОВ")
    paragraphs = iter_paragraphs(doc)
    start = paragraph_index(doc, heading) + 1
    sources = []
    for paragraph in paragraphs[start:]:
        if paragraph.style.name == "Heading 3":
            break
        if paragraph.text.strip():
            sources.append(paragraph)

    for index, paragraph in enumerate(sources, start=1):
        raw = re.sub(r"^(?:\d+\.\s*)+", "", paragraph.text.strip())
        replace_paragraph_text(paragraph, f"{index}. {raw}")
        paragraph.style = "Normal"
        paragraph.alignment = WD_ALIGN_PARAGRAPH.JUSTIFY
        paragraph.paragraph_format.first_line_indent = Pt(0)
        paragraph.paragraph_format.left_indent = Pt(0)
        paragraph.paragraph_format.right_indent = Pt(0)
        paragraph.paragraph_format.space_before = Pt(0)
        paragraph.paragraph_format.space_after = Pt(0)
        paragraph.paragraph_format.line_spacing = 1.5


def remove_caption_with_image(doc: Document, caption_fragment: str) -> None:
    for paragraph in list(doc.paragraphs):
        if caption_fragment not in paragraph.text:
            continue
        previous = paragraph._element.getprevious()
        if previous is not None and previous.tag.endswith("}p"):
            previous_paragraph = Paragraph(previous, paragraph._parent)
            if not previous_paragraph.text.strip():
                delete_paragraph(previous_paragraph)
        delete_paragraph(paragraph)
        break


def insert_new_figures(doc: Document) -> None:
    remove_caption_with_image(doc, "Обзорная диаграмма единого WebUI-драйвера")
    remove_caption_with_image(doc, "Диаграмма взаимодействия Web UI, backend, Unity runtime и physical runtime")
    remove_caption_with_image(doc, "Use-case диаграмма сценариев человека-оператора")
    remove_caption_with_image(doc, "Use-case диаграмма сценариев AI и модели управления")
    remove_caption_with_image(doc, "Use-case диаграмма сценариев оператора для real-robot")
    remove_caption_with_image(doc, "Use-case диаграмма сценариев оператора для unity-sim")
    remove_caption_with_image(doc, "Use-case диаграмма AI-сценариев в Unity")
    remove_caption_with_image(doc, "Use-case диаграмма AI-сценариев sim-to-real")

    caption_2 = find_paragraph(doc, "Рисунок 2 - Диаграмма последовательности подключения оператора и отправки команд")
    inserted_4 = insert_picture_after(
        caption_2,
        SYSTEM_OVERVIEW_PNG,
        16.0,
        "Рисунок 3 - Обзорная диаграмма единого WebUI-драйвера, Unity-симулятора и реального KS0223",
    )
    insert_picture_after(
        inserted_4,
        SYSTEM_INTERACTION_PNG,
        16.0,
        "Рисунок 4 - Диаграмма взаимодействия Web UI, backend, Unity runtime и physical runtime",
    )

    human_real_anchor = find_paragraph(doc, SCENARIO_TEXT[0])
    insert_picture_after(
        human_real_anchor,
        USE_CASE_HUMAN_REAL_PNG,
        16.0,
        "Рисунок 8 - Use-case диаграмма сценариев оператора для real-robot",
    )
    human_sim_anchor = find_paragraph(doc, SCENARIO_TEXT[1])
    insert_picture_after(
        human_sim_anchor,
        USE_CASE_HUMAN_SIM_PNG,
        16.0,
        "Рисунок 9 - Use-case диаграмма сценариев оператора для unity-sim",
    )
    ai_sim_anchor = find_paragraph(doc, SCENARIO_TEXT[2])
    insert_picture_after(
        ai_sim_anchor,
        USE_CASE_AI_SIM_PNG,
        16.0,
        "Рисунок 10 - Use-case диаграмма AI-сценариев в Unity",
    )
    ai_transfer_anchor = find_paragraph(doc, SCENARIO_TEXT[3])
    insert_picture_after(
        ai_transfer_anchor,
        USE_CASE_AI_TRANSFER_PNG,
        16.0,
        "Рисунок 11 - Use-case диаграмма AI-сценариев sim-to-real",
    )


def update_figure_numbers(doc: Document) -> None:
    replacements = {
        "Рисунок 3 - Роботизированная платформа Keyestudio KS0223 (временная иллюстрация, подлежит замене на фото фактического стенда)": "Рисунок 5 - Роботизированная платформа Keyestudio KS0223 (временная иллюстрация, подлежит замене на фото фактического стенда)",
        "Рисунок 4 - Основной экран WebUI-драйвера в режиме управления платформой KS0223": "Рисунок 6 - Основной экран WebUI-драйвера в режиме управления платформой KS0223",
        "Рисунок 5 - Экран журналирования, диагностики и управления логами": "Рисунок 7 - Экран журналирования, диагностики и управления логами",
        "Рисунок 6 - Пример состава набора KS0223 по документации Keyestudio": "Рисунок 12 - Пример состава набора KS0223 по документации Keyestudio",
        "Рисунок 10 - Пример состава набора KS0223 по документации Keyestudio": "Рисунок 12 - Пример состава набора KS0223 по документации Keyestudio",
    }
    for paragraph in doc.paragraphs:
        if paragraph.text.strip() in replacements:
            replace_paragraph_text(paragraph, replacements[paragraph.text.strip()])
            apply_figure_caption_style(paragraph)

    robot_paragraph = find_paragraph_contains(doc, "временное изображение платформы KS0223")
    if robot_paragraph is not None:
        replace_paragraph_text(
            robot_paragraph,
            "На рисунке 5 приведено временное изображение платформы KS0223. В финальной версии отчёта его целесообразно заменить фотографией фактического стенда, используемого при демонстрации. Рисунки 6 и 7 показывают основной экран оператора и страницу журналирования текущего продукта.",
        )
        apply_normal_style(robot_paragraph)

    appendix_paragraph = find_paragraph_contains(doc, "пример состава набора KS0223 по официальной документации производителя")
    if appendix_paragraph is not None:
        replace_paragraph_text(
            appendix_paragraph,
            "На рисунке 12 приведён пример состава набора KS0223 по официальной документации производителя. Изображение используется как вспомогательная иллюстрация состава платформы и может быть заменено на фотографии фактической установки и подключённых периферийных модулей.",
        )
        apply_normal_style(appendix_paragraph)


def update_heading_title(doc: Document) -> None:
    heading = find_paragraph_contains(doc, "УСТРАНЕНИЕ КРИТИЧЕСКОЙ ПРОБЛЕМЫ ПОСЛЕ REBOOT")
    if heading is None:
        heading = find_paragraph(doc, "ОГРАНИЧЕНИЯ ШТАТНОГО ПО KS0223 И ВЫПОЛНЕННЫЕ ДОРАБОТКИ")
    replace_paragraph_text(heading, "ОГРАНИЧЕНИЯ ШТАТНОГО ПО KS0223 И ВЫПОЛНЕННЫЕ ДОРАБОТКИ")
    apply_main_heading_style(heading)


def fix_minor_text(doc: Document) -> None:
    for paragraph in doc.paragraphs:
        if "magister sim-to-real" in paragraph.text:
            prefix, _separator, _suffix = paragraph.text.partition("оформлены")
            replace_paragraph_text(
                paragraph,
                f"{prefix}оформлены архитектурные материалы, рисунки, листинги и отчётные документы, связывающие практику с общей магистерской sim-to-real линией проекта.",
            )
            apply_list_style(paragraph)
            break


def restore_title_page_format(doc: Document) -> None:
    title_indexes = [0, 1, 3, 4, 5, 6, 18, 19, 20, 21, 22, 23, 32]
    for idx in title_indexes:
        paragraph = doc.paragraphs[idx]
        paragraph.style = "Normal"
        paragraph.alignment = WD_ALIGN_PARAGRAPH.CENTER
        paragraph.paragraph_format.first_line_indent = Pt(0)
        paragraph.paragraph_format.left_indent = Pt(0)
        paragraph.paragraph_format.right_indent = Pt(0)
        paragraph.paragraph_format.space_before = Pt(0)
        paragraph.paragraph_format.space_after = Pt(0)
        paragraph.paragraph_format.line_spacing = 1.0


def main() -> None:
    generate_diagrams()

    doc = Document(REPORT_PATH)

    update_heading_title(doc)
    ensure_section_text(doc, "ПРОДУКТОВАЯ АРХИТЕКТУРА", ARCHITECTURE_TEXT)
    ensure_section_text(doc, "СЦЕНАРИИ ИСПОЛЬЗОВАНИЯ И ДАЛЬНЕЙШЕГО РАЗВИТИЯ", SCENARIO_TEXT)
    ensure_section_text(doc, "ОГРАНИЧЕНИЯ ШТАТНОГО ПО KS0223 И ВЫПОЛНЕННЫЕ ДОРАБОТКИ", LIMITATIONS_TEXT)

    insert_new_figures(doc)
    update_figure_numbers(doc)
    renumber_results(doc)
    renumber_sources(doc)
    normalize_paragraphs(doc)
    fix_minor_text(doc)
    restore_title_page_format(doc)

    doc.save(REPORT_PATH)


if __name__ == "__main__":
    main()
