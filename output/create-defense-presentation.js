const pptxgen = require("pptxgenjs");
const fs = require("fs");
const path = require("path");

const pres = new pptxgen();
pres.layout = "LAYOUT_16x9";
pres.author = "Горовенко Никита Михайлович";
pres.title = "Расширяемая платформа симуляции для обучения автономного управления робототехнической платформой KS0223";

// ========== CONSTANTS ==========
const BASE = "<repo>";

// Color palette - Dark premium tech theme (matches Web UI aesthetic)
const C = {
  bg: "0D1117",        // deep dark
  bgCard: "161B22",    // card background
  bgLight: "21262D",   // lighter card
  accent: "00BFA6",    // teal accent (matches web ui green)
  accentDim: "0D9488", // dimmed teal
  white: "E6EDF3",     // off-white text
  gray: "8B949E",      // muted text
  title: "FFFFFF",     // pure white for titles
  bgTitle: "0A0F14",   // darkest for title slide
  orange: "F78166",    // warm accent
  blue: "58A6FF",      // blue accent
  purple: "BC8CFF",    // purple accent
};

// Fonts
const F = {
  title: "Georgia",
  body: "Calibri",
};

// Helper: create fresh shadow
const cardShadow = () => ({ type: "outer", color: "000000", blur: 8, offset: 3, angle: 135, opacity: 0.3 });

// Helper: add slide number
function addSlideNumber(slide, num, total) {
  slide.addText(`${num} / ${total}`, {
    x: 9.0, y: 5.2, w: 0.8, h: 0.3,
    fontSize: 9, fontFace: F.body, color: C.gray, align: "right",
  });
}

// Helper: add title bar at top
function addTitleBar(slide, title) {
  // Accent line at top
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 10, h: 0.06,
    fill: { color: C.accent },
  });
  slide.addText(title, {
    x: 0.6, y: 0.25, w: 8.8, h: 0.55,
    fontSize: 28, fontFace: F.title, color: C.title, bold: true, margin: 0,
  });
}

// Helper: white card with content
function addCard(slide, x, y, w, h) {
  slide.addShape(pres.shapes.RECTANGLE, {
    x, y, w, h,
    fill: { color: C.bgCard },
    shadow: cardShadow(),
  });
}

const TOTAL_SLIDES = 16;
let slideNum = 0;

// ====================================================================
// SLIDE 1: TITLE
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bgTitle };

  // Large accent rectangle at top
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 10, h: 0.08,
    fill: { color: C.accent },
  });

  // University
  slide.addText("Сибирский федеральный университет", {
    x: 0.5, y: 0.4, w: 9, h: 0.35,
    fontSize: 13, fontFace: F.body, color: C.gray, align: "center",
  });
  slide.addText("Институт космических и информационных технологий", {
    x: 0.5, y: 0.7, w: 9, h: 0.35,
    fontSize: 12, fontFace: F.body, color: C.gray, align: "center",
  });

  // Main title
  slide.addText("Разработка расширяемой программной платформы симуляции\nдля обучения автономного управления\nробототехнической платформой KS0223", {
    x: 0.5, y: 1.2, w: 9, h: 1.5,
    fontSize: 22, fontFace: F.title, color: C.title, bold: true,
    align: "center", valign: "middle", lineSpacingMultiple: 1.2,
  });

  // Subtitle
  slide.addText("Магистерская диссертация", {
    x: 0.5, y: 2.85, w: 9, h: 0.4,
    fontSize: 14, fontFace: F.body, color: C.accent, align: "center", bold: true,
  });

  // Author info
  slide.addText([
    { text: "Выполнил:  ", options: { color: C.gray, fontSize: 13 } },
    { text: "Горовенко Никита Михайлович", options: { color: C.white, fontSize: 13, bold: true } },
    { text: "\nНаправление:  ", options: { color: C.gray, fontSize: 12, breakLine: false } },
    { text: "09.04.04 Программная инженерия", options: { color: C.white, fontSize: 12 } },
  ], {
    x: 1.5, y: 3.5, w: 7, h: 0.9,
    fontFace: F.body, align: "center", lineSpacingMultiple: 1.5,
  });

  slide.addText("Красноярск, 2026", {
    x: 0.5, y: 5.1, w: 9, h: 0.3,
    fontSize: 11, fontFace: F.body, color: C.gray, align: "center",
  });

  slide.addNotes("Здравствуйте, уважаемые члены комиссии! Меня зовут Горовенко Никита, я представляю магистерскую диссертацию на тему разработки расширяемой программной платформы симуляции для обучения автономного управления робототехнической платформой KS0223. Работа выполнена в рамках направления 09.04.04 Программная инженерия.");
}

// ====================================================================
// SLIDE 2: PROBLEM STATEMENT
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Актуальность и проблема");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Left column - problem cards
  const problems = [
    { icon: "!", title: "Дорого", desc: "Реальные испытания требуют\nфизического оборудования\nи могут повредить робота" },
    { icon: "!", title: "Медленно", desc: "Каждый эксперимент в реальном\nвремени, невозможно ускорить\nсбор данных для обучения" },
    { icon: "!", title: "Невоспроизводимо", desc: "Условия реального мира\nменяются между запусками,\nсравнение затруднено" },
  ];

  problems.forEach((p, i) => {
    const yOff = 1.1 + i * 1.35;
    addCard(slide, 0.5, yOff, 4.2, 1.15);
    // Accent left border
    slide.addShape(pres.shapes.RECTANGLE, {
      x: 0.5, y: yOff, w: 0.07, h: 1.15,
      fill: { color: C.orange },
    });
    slide.addText(p.title, {
      x: 0.8, y: yOff + 0.08, w: 3.7, h: 0.35,
      fontSize: 16, fontFace: F.body, color: C.orange, bold: true, margin: 0,
    });
    slide.addText(p.desc, {
      x: 0.8, y: yOff + 0.4, w: 3.7, h: 0.7,
      fontSize: 11, fontFace: F.body, color: C.gray, margin: 0, lineSpacingMultiple: 1.2,
    });
  });

  // Right column - solution
  addCard(slide, 5.2, 1.1, 4.3, 4.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 1.1, w: 0.07, h: 4.2,
    fill: { color: C.accent },
  });
  slide.addText("Решение: симуляция", {
    x: 5.5, y: 1.2, w: 3.8, h: 0.45,
    fontSize: 18, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  const solutions = [
    "Безопасная среда для обучения\nбез риска повреждения оборудования",
    "Ускоренный сбор данных:\nвремя симуляции можно масштабировать",
    "Полная воспроизводимость:\nсценарии и seed для повторения",
    "Расширяемость: новые роботы,\nтрассы, сенсоры через плагины",
  ];
  solutions.forEach((s, i) => {
    slide.addText(s, {
      x: 5.5, y: 1.75 + i * 0.85, w: 3.8, h: 0.75,
      fontSize: 11, fontFace: F.body, color: C.white, margin: 0, lineSpacingMultiple: 1.2,
      bullet: { code: "2713" },
    });
  });

  slide.addNotes("Обучение автономному управлению на реальном роботе сопряжено с тремя ключевыми проблемами:\n1. Высокая стоимость — реальные испытания требуют физического оборудования и могут повредить робота.\n2. Низкая скорость — каждый эксперимент проходит в реальном времени, невозможно ускорить сбор данных.\n3. Невоспроизводимость — условия реального мира меняются между запусками.\n\nСимуляция решает все три проблемы: безопасная среда, масштабируемое время, полная воспроизводимость. Наша платформа добавляет расширяемость через плагины.");
}

// ====================================================================
// SLIDE 3: GOALS AND TASKS
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Цель и задачи работы");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Goal box
  addCard(slide, 0.5, 1.1, 9.0, 0.9);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 1.1, w: 9.0, h: 0.07,
    fill: { color: C.accent },
  });
  slide.addText("Цель", {
    x: 0.7, y: 1.2, w: 1.0, h: 0.35,
    fontSize: 12, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  slide.addText("Разработать расширяемую платформу симуляции в Unity для проведения экспериментов по обучению автономного управления и sim-to-real трансферу для робототехнической платформы KS0223", {
    x: 0.7, y: 1.5, w: 8.6, h: 0.45,
    fontSize: 12, fontFace: F.body, color: C.white, margin: 0,
  });

  // Tasks - 2 columns
  const tasksLeft = [
    "Спроектировать модульную архитектуру\nсимулятора (runtime, backend, CLI, Python)",
    "Реализовать физическую модель робота\nKS0223 с набором сенсоров",
    "Разработать систему плагинов для\nрасширения каталога роботов и трасс",
  ];
  const tasksRight = [
    "Реализовать полный цикл модели:\nобучение, загрузка, активация, запуск",
    "Интегрировать платформу с ROS2\nдля sim-to-real трансфера",
    "Провести экспериментальную оценку\nкачества обученных моделей",
  ];

  tasksLeft.forEach((t, i) => {
    const y = 2.3 + i * 1.05;
    addCard(slide, 0.5, y, 4.3, 0.85);
    slide.addText(`${i + 1}`, {
      x: 0.65, y: y + 0.1, w: 0.4, h: 0.4,
      fontSize: 18, fontFace: F.title, color: C.accent, bold: true, align: "center", margin: 0,
    });
    slide.addText(t, {
      x: 1.1, y: y + 0.1, w: 3.5, h: 0.65,
      fontSize: 11, fontFace: F.body, color: C.white, margin: 0, lineSpacingMultiple: 1.2,
    });
  });

  tasksRight.forEach((t, i) => {
    const y = 2.3 + i * 1.05;
    addCard(slide, 5.2, y, 4.3, 0.85);
    slide.addText(`${i + 4}`, {
      x: 5.35, y: y + 0.1, w: 0.4, h: 0.4,
      fontSize: 18, fontFace: F.title, color: C.accent, bold: true, align: "center", margin: 0,
    });
    slide.addText(t, {
      x: 5.8, y: y + 0.1, w: 3.5, h: 0.65,
      fontSize: 11, fontFace: F.body, color: C.white, margin: 0, lineSpacingMultiple: 1.2,
    });
  });

  slide.addNotes("Цель работы — разработать расширяемую платформу симуляции в Unity для обучения автономного управления KS0223.\n\nЗадачи:\n1. Модульная архитектура из 4 компонентов\n2. Физическая модель робота с камерой, ультразвуком, датчиком линии\n3. Система плагинов для расширения\n4. Полный model lifecycle\n5. ROS2 интеграция\n6. Экспериментальная оценка");
}

// ====================================================================
// SLIDE 4: ANALOGS COMPARISON
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Обзор аналогов");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  const headers = [
    { text: "Критерий", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 10, fontFace: F.body } },
    { text: "CARLA", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 10, fontFace: F.body } },
    { text: "AirSim", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 10, fontFace: F.body } },
    { text: "Gazebo", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 10, fontFace: F.body } },
    { text: "uav-simulator", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 10, fontFace: F.body } },
  ];

  const makeRow = (criteria, v1, v2, v3, v4, highlight) => {
    const bgRow = highlight ? C.bgCard : C.bgLight;
    return [
      { text: criteria, options: { fill: { color: bgRow }, color: C.white, fontSize: 10, fontFace: F.body } },
      { text: v1, options: { fill: { color: bgRow }, color: C.gray, fontSize: 10, fontFace: F.body, align: "center" } },
      { text: v2, options: { fill: { color: bgRow }, color: C.gray, fontSize: 10, fontFace: F.body, align: "center" } },
      { text: v3, options: { fill: { color: bgRow }, color: C.gray, fontSize: 10, fontFace: F.body, align: "center" } },
      { text: v4, options: { fill: { color: bgRow }, color: C.accent, fontSize: 10, fontFace: F.body, align: "center", bold: true } },
    ];
  };

  const tableData = [
    headers,
    makeRow("Движок", "UE4", "UE4", "Собственный", "Unity 6", false),
    makeRow("Целевые ТС", "Автомобили", "Дроны, авто", "Произвольные", "Роботы, авто", true),
    makeRow("Расширяемость", "Средняя", "Низкая", "Высокая", "Плагины", false),
    makeRow("Python API", "Есть", "Есть", "Частично", "Есть", true),
    makeRow("ROS2 интеграция", "Через мост", "Нет", "Нативная", "Мост", false),
    makeRow("Model lifecycle", "Нет", "Нет", "Нет", "E2E pipeline", true),
    makeRow("Web UI оператора", "Нет", "Нет", "Частично", "Полный", false),
    makeRow("Sim-to-real", "Ограниченно", "Нет", "Частично", "Есть", true),
    makeRow("Сценарии (YAML)", "JSON", "JSON", "SDF/URDF", "YAML", false),
    makeRow("Лицензия", "MIT", "MIT*", "Apache 2", "Проприетарная", true),
  ];

  slide.addTable(tableData, {
    x: 0.4, y: 1.1, w: 9.2,
    colW: [2.0, 1.6, 1.6, 1.6, 2.4],
    border: { pt: 0.5, color: "30363D" },
    rowH: [0.35, 0.35, 0.35, 0.35, 0.35, 0.35, 0.35, 0.35, 0.35, 0.35, 0.35],
    margin: [4, 6, 4, 6],
  });

  slide.addNotes("Основные аналоги:\n\n1. CARLA — open-source симулятор на UE4 для автомобилей. Богатый функционал, но привязан к автомобилям, нет model lifecycle, нет Web UI.\n\n2. AirSim — Microsoft, для дронов и автомобилей, но проект архивирован, низкая расширяемость.\n\n3. Gazebo — стандарт в ROS-экосистеме, но собственный движок со слабой графикой, нет Web UI, нет model lifecycle.\n\nНаша платформа выигрывает в: полном E2E pipeline от обучения до деплоя, Web UI оператора, системе плагинов, интеграции с ROS2. Ключевое отличие — полный model lifecycle management, которого нет ни у одного аналога.");
}

// ====================================================================
// SLIDE 5: ARCHITECTURE OVERVIEW
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Архитектура платформы");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // 4 component boxes in a flow
  const components = [
    { name: "Unity Runtime", desc: "Физика, сенсоры,\nHTTP JSON API", color: C.blue, x: 0.5, y: 1.5 },
    { name: "Backend", desc: "ASP.NET Core,\nSignalR, ONNX", color: C.accent, x: 2.8, y: 1.5 },
    { name: "Web UI", desc: "React + MUI,\nоператорский пульт", color: C.purple, x: 5.1, y: 1.5 },
    { name: "Python", desc: "RL обучение,\nONNX экспорт, KPI", color: C.orange, x: 7.4, y: 1.5 },
  ];

  components.forEach((c) => {
    addCard(slide, c.x, c.y, 2.0, 1.4);
    slide.addShape(pres.shapes.RECTANGLE, {
      x: c.x, y: c.y, w: 2.0, h: 0.06,
      fill: { color: c.color },
    });
    slide.addText(c.name, {
      x: c.x + 0.1, y: c.y + 0.15, w: 1.8, h: 0.35,
      fontSize: 14, fontFace: F.body, color: c.color, bold: true, align: "center", margin: 0,
    });
    slide.addText(c.desc, {
      x: c.x + 0.1, y: c.y + 0.55, w: 1.8, h: 0.7,
      fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0, lineSpacingMultiple: 1.3,
    });
  });

  // Arrows between components (simple lines)
  // Unity -> Backend
  slide.addShape(pres.shapes.LINE, {
    x: 2.5, y: 2.2, w: 0.3, h: 0,
    line: { color: C.gray, width: 1.5, dashType: "dash" },
  });
  // Backend -> Web UI
  slide.addShape(pres.shapes.LINE, {
    x: 4.8, y: 2.2, w: 0.3, h: 0,
    line: { color: C.gray, width: 1.5, dashType: "dash" },
  });
  // Python -> Unity (dashed below)
  slide.addShape(pres.shapes.LINE, {
    x: 7.1, y: 2.2, w: 0.3, h: 0,
    line: { color: C.gray, width: 1.5, dashType: "dash" },
  });

  // CLI box below
  addCard(slide, 3.5, 3.3, 3.0, 0.9);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 3.5, y: 3.3, w: 3.0, h: 0.06,
    fill: { color: C.accent },
  });
  slide.addText("rusim CLI", {
    x: 3.6, y: 3.4, w: 2.8, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.accent, bold: true, align: "center", margin: 0,
  });
  slide.addText("Единая точка входа: сборка, запуск,\nсценарии, модели, плагины", {
    x: 3.6, y: 3.7, w: 2.8, h: 0.45,
    fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0, lineSpacingMultiple: 1.2,
  });

  // Key APIs description
  addCard(slide, 0.5, 4.45, 9.0, 0.9);
  slide.addText([
    { text: "HTTP JSON API  ", options: { color: C.blue, bold: true, fontSize: 11 } },
    { text: "/health  /contract  /reset  /step", options: { color: C.gray, fontSize: 11 } },
    { text: "     |     ", options: { color: "30363D", fontSize: 11 } },
    { text: "REST API  ", options: { color: C.accent, bold: true, fontSize: 11 } },
    { text: "/api/models/*  /api/autopilot/*", options: { color: C.gray, fontSize: 11 } },
  ], {
    x: 0.7, y: 4.55, w: 8.6, h: 0.4,
    fontFace: F.body, align: "center", margin: 0,
  });
  slide.addText("SignalR для real-time обновлений  |  Поддержка режимов unity-sim и real-robot", {
    x: 0.7, y: 4.95, w: 8.6, h: 0.3,
    fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0,
  });

  slide.addNotes("Архитектура платформы состоит из четырех основных компонентов:\n\n1. Unity Runtime — ядро симуляции. Физика, сенсоры, HTTP JSON API для управления. Эндпоинты: /health, /contract, /reset, /step.\n\n2. Backend на ASP.NET Core — оператор-сервер. Управление моделями (ONNX), автопилотом, сессиями. SignalR для real-time. Поддерживает два runtime provider: unity-sim и real-robot.\n\n3. Web UI — React-приложение с MUI. Операторский пульт: камера, телеметрия, управление моделями.\n\n4. Python tooling — обучение моделей, экспорт ONNX, KPI-оценка.\n\nВсё связано через rusim CLI — единую точку входа для разработчика.");
}

// ====================================================================
// SLIDE 6: UNITY RUNTIME + SCREENSHOT
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Unity Runtime: симуляция");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Left side - features
  const features = [
    { title: "Физический движок", desc: "Unity 6 Rigidbody, настраиваемая\nфизика для каждого робота" },
    { title: "Step-based API", desc: "POST /step: throttle, steer, brake\nОтвет: state + reward + done + frame" },
    { title: "Каталог контрактов", desc: "GET /contract: доступные роботы,\nтрассы, сенсоры, актуаторы" },
    { title: "Сценарии YAML", desc: "Конфигурация трека, робота, сенсоров,\nмаршрута, логирования" },
  ];

  features.forEach((f, i) => {
    const y = 1.15 + i * 1.0;
    slide.addText(f.title, {
      x: 0.6, y: y, w: 3.8, h: 0.3,
      fontSize: 13, fontFace: F.body, color: C.accent, bold: true, margin: 0,
    });
    slide.addText(f.desc, {
      x: 0.6, y: y + 0.28, w: 3.8, h: 0.6,
      fontSize: 10, fontFace: F.body, color: C.gray, margin: 0, lineSpacingMultiple: 1.2,
    });
  });

  // Right side - screenshot of arena
  addCard(slide, 5.0, 1.1, 4.5, 2.6);
  slide.addImage({
    path: path.join(BASE, "docs/reports/presentation/unity-overview.png"),
    x: 5.1, y: 1.2, w: 4.3, h: 2.4,
    sizing: { type: "contain", w: 4.3, h: 2.4 },
  });

  // Robot closeup below
  addCard(slide, 5.0, 3.9, 4.5, 1.5);
  slide.addImage({
    path: path.join(BASE, "docs/reports/presentation/unity-robot-closeup.png"),
    x: 5.1, y: 4.0, w: 4.3, h: 1.3,
    sizing: { type: "contain", w: 4.3, h: 1.3 },
  });

  slide.addNotes("Unity Runtime — ядро платформы на Unity 6.\n\nStep-based API: каждый вызов POST /step принимает throttle, steer, brake и возвращает полное состояние: позиция, скорость, сенсоры, camera frame в base64.\n\nНа скриншотах видна арена с разметкой и робот KS0223. Трасса имеет повороты для тестирования навигации.\n\nСценарии описываются в YAML: трек, робот, сенсоры, маршрут. Полная воспроизводимость через seed.");
}

// ====================================================================
// SLIDE 7: KS0223 ROBOT MODEL
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Робот KS0223: модель и сенсоры");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Big stat cards at top - equal widths
  const stats = [
    { value: "2.2", unit: "м/с", label: "Макс. скорость" },
    { value: "4.0", unit: "м/с\u00B2", label: "Ускорение" },
    { value: "1280x720", unit: "px", label: "Камера" },
    { value: "5", unit: "сегм.", label: "Датчик линии" },
  ];

  stats.forEach((s, i) => {
    const x = 0.5 + i * 2.3;
    addCard(slide, x, 1.1, 2.0, 1.1);
    slide.addText(s.value, {
      x: x + 0.1, y: 1.15, w: 1.8, h: 0.5,
      fontSize: 22, fontFace: F.title, color: C.accent, bold: true, align: "center", margin: 0,
    });
    slide.addText(s.unit, {
      x: x + 0.1, y: 1.6, w: 1.8, h: 0.2,
      fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0,
    });
    slide.addText(s.label, {
      x: x + 0.1, y: 1.8, w: 1.8, h: 0.25,
      fontSize: 10, fontFace: F.body, color: C.white, align: "center", margin: 0,
    });
  });

  // Sensors detail
  addCard(slide, 0.5, 2.5, 4.3, 2.7);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 2.5, w: 4.3, h: 0.06,
    fill: { color: C.blue },
  });
  slide.addText("Сенсорное оснащение", {
    x: 0.7, y: 2.6, w: 3.9, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.blue, bold: true, margin: 0,
  });

  const sensors = [
    { name: "Фронтальная камера", spec: "1280x720 JPEG, 95% качество" },
    { name: "Датчик линии", spec: "5 сегментов, нормализованный массив" },
    { name: "Ультразвуковой датчик", spec: "Дальность 3.5 м, фронтальный" },
    { name: "Одометрия", spec: "Позиция, скорость, поворот" },
    { name: "Спидометр", spec: "Скорость в м/с" },
  ];

  sensors.forEach((s, i) => {
    slide.addText(s.name, {
      x: 0.7, y: 3.05 + i * 0.4, w: 1.8, h: 0.3,
      fontSize: 10, fontFace: F.body, color: C.white, bold: true, margin: 0,
    });
    slide.addText(s.spec, {
      x: 2.5, y: 3.05 + i * 0.4, w: 2.1, h: 0.3,
      fontSize: 10, fontFace: F.body, color: C.gray, margin: 0,
    });
  });

  // Control model
  addCard(slide, 5.2, 2.5, 4.3, 2.7);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 2.5, w: 4.3, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Модель управления", {
    x: 5.4, y: 2.6, w: 3.9, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });

  slide.addText([
    { text: "Дифференциальный привод", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "2 мотора с независимым PWM", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 6, breakLine: true } },
    { text: "Высокоуровневое управление", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "throttle [-1..1], steer [-1..1], brake [0..1]", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 6, breakLine: true } },
    { text: "Низкоуровневое управление", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "left_pwm_norm, right_pwm_norm [-1..1]", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 6, breakLine: true } },
    { text: "Физика", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "Rigidbody, linear/angular damping", options: { color: C.gray, fontSize: 9 } },
  ], {
    x: 5.4, y: 3.05, w: 3.9, h: 2.0,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.15,
  });

  slide.addNotes("Робот KS0223 — двухколёсный дифференциальный робот Keyestudio.\n\nОсновные параметры: макс. скорость 2.2 м/с, ускорение 4.0 м/с².\n\nСенсоры: фронтальная камера 1280x720, 5-сегментный датчик линии, ультразвуковой дальномер до 3.5 м, одометрия.\n\nДва режима управления: высокоуровневый (throttle/steer/brake для нейросети) и низкоуровневый (PWM для прямого управления моторами). Физика реализована через Unity Rigidbody с настроенным демпфированием.");
}

// ====================================================================
// SLIDE 8: OPERATOR STACK (BACKEND + WEB UI)
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Оператор: Backend + Web UI");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Left - backend architecture
  addCard(slide, 0.5, 1.1, 4.3, 1.8);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 1.1, w: 0.07, h: 1.8,
    fill: { color: C.accent },
  });
  slide.addText("ASP.NET Core Backend", {
    x: 0.75, y: 1.15, w: 3.8, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  const services = [
    "ModelRegistryService — загрузка, каталог, активация моделей",
    "AutopilotService — inference loop с ONNX Runtime",
    "RuntimeProvider — абстракция unity-sim / real-robot",
    "SessionManager — мульти-клиентские сессии",
  ];
  services.forEach((s, i) => {
    slide.addText(s, {
      x: 0.75, y: 1.55 + i * 0.3, w: 3.85, h: 0.25,
      fontSize: 9.5, fontFace: F.body, color: C.gray, margin: 0, bullet: true,
    });
  });

  // Left bottom - Web UI features
  addCard(slide, 0.5, 3.15, 4.3, 2.15);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 3.15, w: 0.07, h: 2.15,
    fill: { color: C.purple },
  });
  slide.addText("React Web UI", {
    x: 0.75, y: 3.2, w: 3.8, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.purple, bold: true, margin: 0,
  });
  const uiFeatures = [
    "Операторский пульт управления",
    "Видеопоток с камеры робота (MJPEG)",
    "Визуализация телеметрии в реальном времени",
    "Загрузка и активация моделей",
    "Запуск/остановка автопилота",
    "Подключение к unity-sim или real-robot",
  ];
  uiFeatures.forEach((f, i) => {
    slide.addText(f, {
      x: 0.75, y: 3.6 + i * 0.27, w: 3.85, h: 0.22,
      fontSize: 9.5, fontFace: F.body, color: C.gray, margin: 0, bullet: true,
    });
  });

  // Right - screenshot of dashboard
  addCard(slide, 5.2, 1.1, 4.3, 4.2);
  slide.addImage({
    path: path.join(BASE, "docs/report/prediploma-practice/evidence/dashboard-page.png"),
    x: 5.3, y: 1.2, w: 4.1, h: 4.0,
    sizing: { type: "contain", w: 4.1, h: 4.0 },
  });

  slide.addNotes("Оператор-стек состоит из backend и Web UI.\n\nBackend на ASP.NET Core включает:\n- ModelRegistryService — управление моделями в формате ONNX\n- AutopilotService — запуск inference loop через ONNX Runtime\n- RuntimeProvider — абстракция для переключения между симулятором и реальным роботом\n- SessionManager — поддержка нескольких клиентов\n\nWeb UI на React предоставляет полный операторский пульт: видео с камеры, телеметрия, управление моделями. На скриншоте виден интерфейс Control Center с подключением, control pad и секцией камеры.");
}

// ====================================================================
// SLIDE 9: MODEL LIFECYCLE
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Model Lifecycle: от обучения до запуска");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Pipeline flow
  const steps = [
    { label: "Train", desc: "RL обучение\nв Python", color: C.orange },
    { label: "Export", desc: "ONNX +\nmetadata", color: C.orange },
    { label: "Upload", desc: "Загрузка\nв backend", color: C.accent },
    { label: "Activate", desc: "Выбор\nверсии", color: C.accent },
    { label: "Run", desc: "Autopilot\ninference", color: C.blue },
    { label: "Evaluate", desc: "KPI\nоценка", color: C.purple },
  ];

  steps.forEach((s, i) => {
    const x = 0.4 + i * 1.55;
    addCard(slide, x, 1.2, 1.3, 1.4);
    slide.addShape(pres.shapes.RECTANGLE, {
      x: x, y: 1.2, w: 1.3, h: 0.06,
      fill: { color: s.color },
    });
    slide.addText(s.label, {
      x: x + 0.05, y: 1.35, w: 1.2, h: 0.35,
      fontSize: 14, fontFace: F.body, color: s.color, bold: true, align: "center", margin: 0,
    });
    slide.addText(s.desc, {
      x: x + 0.05, y: 1.75, w: 1.2, h: 0.6,
      fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0, lineSpacingMultiple: 1.2,
    });
    // Arrow to next
    if (i < steps.length - 1) {
      slide.addText("\u2192", {
        x: x + 1.3, y: 1.6, w: 0.25, h: 0.4,
        fontSize: 16, fontFace: F.body, color: C.gray, align: "center", margin: 0,
      });
    }
  });

  // Model artifact format
  addCard(slide, 0.5, 3.0, 4.3, 2.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 3.0, w: 4.3, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Артефакт модели", {
    x: 0.7, y: 3.1, w: 3.9, h: 0.35,
    fontSize: 13, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });
  slide.addText([
    { text: "model.onnx", options: { bold: true, color: C.white, fontSize: 11, breakLine: true } },
    { text: "  Нейронная сеть в формате ONNX\n", options: { color: C.gray, fontSize: 10, breakLine: true } },
    { text: "metadata.json", options: { bold: true, color: C.white, fontSize: 11, breakLine: true } },
    { text: "  Схемы входов/выходов, сенсоры\n", options: { color: C.gray, fontSize: 10, breakLine: true } },
    { text: "metrics.json", options: { bold: true, color: C.white, fontSize: 11, breakLine: true } },
    { text: "  KPI обучения, baseline метрики", options: { color: C.gray, fontSize: 10 } },
  ], {
    x: 0.7, y: 3.5, w: 3.9, h: 1.6,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.3,
  });

  // Screenshot of model control page
  addCard(slide, 5.2, 3.0, 4.3, 2.2);
  slide.addImage({
    path: path.join(BASE, "docs/report/prediploma-practice/evidence/model-control-e2e-2026-03-29.png"),
    x: 5.3, y: 3.1, w: 4.1, h: 2.0,
    sizing: { type: "contain", w: 4.1, h: 2.0 },
  });

  slide.addNotes("Ключевое преимущество платформы — полный E2E model lifecycle.\n\nПайплайн:\n1. Train — обучение через RL в Python (Gymnasium + HTTP API к симулятору)\n2. Export — сохранение в ONNX с metadata.json и metrics.json\n3. Upload — загрузка артефакта через REST API или CLI\n4. Activate — выбор конкретной версии модели\n5. Run — запуск autopilot inference loop на backend\n6. Evaluate — сбор KPI через 20 эпизодов\n\nАртефакт модели: model.onnx (нейросеть), metadata.json (описание входов/выходов), metrics.json (метрики обучения). На скриншоте виден интерфейс Model Control с загруженной моделью и работающим автопилотом.");
}

// ====================================================================
// SLIDE 10: PLUGIN SYSTEM
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Система плагинов");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Left - how it works
  addCard(slide, 0.5, 1.1, 4.3, 2.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 1.1, w: 4.3, h: 0.06,
    fill: { color: C.accent },
  });
  slide.addText("Архитектура плагинов", {
    x: 0.7, y: 1.18, w: 3.9, h: 0.3,
    fontSize: 14, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Plugin SDK", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "UPM-пакет с базовыми классами и контрактами", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 5, breakLine: true } },
    { text: "PluginRegistry", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "ScriptableObject: каталог, ID, версионирование", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 5, breakLine: true } },
    { text: "Формат: .rusim-plugin.zip", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "manifest.json + ассеты", options: { color: C.gray, fontSize: 9 } },
  ], {
    x: 0.7, y: 1.55, w: 3.9, h: 1.65,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.15,
  });

  // Right - plugin types
  addCard(slide, 5.2, 1.1, 4.3, 2.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 1.1, w: 4.3, h: 0.06,
    fill: { color: C.purple },
  });
  slide.addText("Типы плагинов", {
    x: 5.4, y: 1.18, w: 3.9, h: 0.3,
    fontSize: 14, fontFace: F.body, color: C.purple, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Vehicle Plugin", options: { bold: true, color: C.white, fontSize: 11, breakLine: true } },
    { text: "Физика, модель управления, сенсоры, визуальная модель робота", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 8, breakLine: true } },
    { text: "Track Plugin", options: { bold: true, color: C.white, fontSize: 11, breakLine: true } },
    { text: "Среда, разметка, границы, точки спавна и маршрут", options: { color: C.gray, fontSize: 9 } },
  ], {
    x: 5.4, y: 1.55, w: 3.9, h: 1.65,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.15,
  });

  // Bottom - catalog
  addCard(slide, 0.5, 3.4, 9.0, 1.8);
  slide.addText("Каталог платформы", {
    x: 0.7, y: 3.5, w: 8.6, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.white, bold: true, margin: 0,
  });

  const catalogHeaders = [
    { text: "ID", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 9, fontFace: F.body } },
    { text: "Тип", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 9, fontFace: F.body } },
    { text: "Описание", options: { fill: { color: C.accentDim }, color: C.title, bold: true, fontSize: 9, fontFace: F.body } },
  ];

  const catalogRows = [
    ["vehicle.ks0223.v1", "Vehicle", "Двухколёсный робот KS0223 (базовый)"],
    ["vehicle.prometeo.sport.v1", "Vehicle", "Четырёхколёсный автомобиль Prometeo"],
    ["vehicle.drone.simple.v1", "Vehicle", "Квадрокоптер Simple Drone"],
    ["track.roadsystem_realistic.v2", "Track", "Реалистичная трасса с Road System"],
    ["track.basic_arena.v1", "Track", "Базовая арена с стенами"],
    ["track.cardboard_corridor.v1", "Track", "A-B коридор для обучения"],
  ];

  const catalogData = [catalogHeaders];
  catalogRows.forEach((row, i) => {
    const bg = i % 2 === 0 ? C.bgCard : C.bgLight;
    catalogData.push(row.map(text => ({
      text, options: { fill: { color: bg }, color: C.gray, fontSize: 9, fontFace: F.body },
    })));
  });

  slide.addTable(catalogData, {
    x: 0.7, y: 3.9, w: 8.6,
    colW: [3.2, 1.2, 4.2],
    border: { pt: 0.5, color: "30363D" },
    margin: [3, 5, 3, 5],
  });

  slide.addNotes("Система плагинов — ключевая особенность архитектуры.\n\nPlugin SDK — UPM-пакет с базовыми классами для создания роботов и трасс. PluginRegistry — ScriptableObject, единый каталог с версионированием.\n\nДва типа плагинов: Vehicle (робот + физика + сенсоры) и Track (среда + маршрут).\n\nСейчас каталог включает 7 роботов (KS0223, автомобили, дрон) и 4 трассы. CLI: rusim plugin new/install/list/remove.\n\nЭто позволяет внешним разработчикам расширять платформу без модификации ядра.");
}

// ====================================================================
// SLIDE 11: PYTHON TRAINING
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Обучение модели в Python");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Training pipeline flow
  addCard(slide, 0.5, 1.1, 9.0, 1.5);
  slide.addText("Pipeline обучения", {
    x: 0.7, y: 1.15, w: 8.6, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });

  const pipeline = [
    { label: "Gymnasium Env", desc: "Обёртка API" },
    { label: "RL Algorithm", desc: "PPO / SAC" },
    { label: "ONNX Export", desc: "torch.onnx" },
    { label: "KPI Eval", desc: "20 эпизодов" },
  ];
  pipeline.forEach((p, i) => {
    const x = 0.8 + i * 2.2;
    slide.addShape(pres.shapes.RECTANGLE, {
      x: x, y: 1.65, w: 1.8, h: 0.7,
      fill: { color: C.bgLight },
    });
    slide.addText(p.label, {
      x: x + 0.05, y: 1.67, w: 1.7, h: 0.35,
      fontSize: 11, fontFace: F.body, color: C.white, bold: true, align: "center", margin: 0,
    });
    slide.addText(p.desc, {
      x: x + 0.05, y: 2.0, w: 1.7, h: 0.3,
      fontSize: 9, fontFace: F.body, color: C.gray, align: "center", margin: 0,
    });
    if (i < pipeline.length - 1) {
      slide.addText("\u2192", {
        x: x + 1.8, y: 1.75, w: 0.4, h: 0.4,
        fontSize: 16, fontFace: F.body, color: C.gray, align: "center", margin: 0,
      });
    }
  });

  // Left - observation space
  addCard(slide, 0.5, 2.9, 4.3, 2.3);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 2.9, w: 4.3, h: 0.06,
    fill: { color: C.blue },
  });
  slide.addText("Observation Space", {
    x: 0.7, y: 2.98, w: 3.9, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.blue, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Vision Policy (мультимодальная):", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "  Camera: 84x84x3 RGB (downsampled)", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "  Ultrasonic: float [0..3.5] m", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 5, breakLine: true } },
    { text: "Flat Policy (числовая):", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "  Speed, steer, distance_to_center,", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "  heading_error, ultrasonic, line_tracker[5]", options: { color: C.gray, fontSize: 9 } },
  ], {
    x: 0.7, y: 3.35, w: 3.9, h: 1.75,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.2,
  });

  // Right - action space + reward
  addCard(slide, 5.2, 2.9, 4.3, 2.3);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 2.9, w: 4.3, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Action Space & Reward", {
    x: 5.4, y: 2.98, w: 3.9, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Action Space:", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "  throttle [-1..1]", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "  steer [-1..1]", options: { color: C.gray, fontSize: 9, breakLine: true } },
    { text: "", options: { fontSize: 5, breakLine: true } },
    { text: "Reward Shaping:", options: { bold: true, color: C.white, fontSize: 10, breakLine: true } },
    { text: "  + progress along route\n", options: { color: C.accent, fontSize: 10, breakLine: true } },
    { text: "  - deviation from centerline", options: { color: C.orange, fontSize: 9, breakLine: true } },
    { text: "  - out of bounds penalty", options: { color: C.orange, fontSize: 9, breakLine: true } },
    { text: "  + goal reached bonus", options: { color: C.accent, fontSize: 9 } },
  ], {
    x: 5.4, y: 3.35, w: 3.9, h: 1.75,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.2,
  });

  slide.addNotes("Обучение модели реализовано через Python tooling.\n\nPipeline: Gymnasium Environment (обёртка над HTTP API) → RL алгоритм (PPO/SAC) → экспорт в ONNX → KPI-оценка на 20 эпизодах.\n\nДва типа observation space:\n1. Vision Policy — мультимодальная: изображение 84x84 RGB + ультразвук\n2. Flat Policy — числовой вектор: скорость, руль, расстояние до центра, ошибка курса, сенсоры\n\nAction space: throttle и steer, оба в диапазоне [-1, 1].\n\nReward shaping: награда за прогресс по маршруту, штраф за отклонение от центральной линии и выход за границы, бонус за достижение цели.");
}

// ====================================================================
// SLIDE 12: ROS2 BRIDGE
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Интеграция с ROS2");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Bridge architecture
  addCard(slide, 0.5, 1.1, 9.0, 1.7);
  slide.addText("ROS2 Bridge: симулятор как ROS2-робот", {
    x: 0.7, y: 1.15, w: 8.6, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });

  // Flow: Unity -> Bridge -> ROS2 Topics
  const bridgeSteps = [
    { label: "Unity\nRuntime", color: C.blue },
    { label: "HTTP\nJSON API", color: C.gray },
    { label: "ros2_bridge.py", color: C.accent },
    { label: "ROS2\nTopics", color: C.orange },
  ];
  bridgeSteps.forEach((b, i) => {
    const x = 1.0 + i * 2.2;
    slide.addShape(pres.shapes.RECTANGLE, {
      x: x, y: 1.7, w: 1.6, h: 0.7,
      fill: { color: C.bgLight },
    });
    slide.addText(b.label, {
      x: x + 0.05, y: 1.75, w: 1.5, h: 0.6,
      fontSize: 11, fontFace: F.body, color: b.color, bold: true, align: "center", margin: 0,
    });
    if (i < bridgeSteps.length - 1) {
      slide.addText("\u2192", {
        x: x + 1.6, y: 1.8, w: 0.6, h: 0.4,
        fontSize: 16, fontFace: F.body, color: C.gray, align: "center", margin: 0,
      });
    }
  });

  // Published topics
  addCard(slide, 0.5, 3.1, 4.3, 2.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 3.1, w: 4.3, h: 0.06,
    fill: { color: C.accent },
  });
  slide.addText("Publish Topics", {
    x: 0.7, y: 3.2, w: 3.9, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  const pubTopics = [
    "/uavsim/ks0223/odom",
    "/uavsim/ks0223/camera/front/image_raw",
    "/uavsim/ks0223/ultrasonic/front",
    "/uavsim/ks0223/line_tracker/front_norm",
    "/uavsim/ks0223/speedometer/mps",
    "/uavsim/ks0223/battery_state",
  ];
  pubTopics.forEach((t, i) => {
    slide.addText(t, {
      x: 0.7, y: 3.55 + i * 0.27, w: 3.9, h: 0.22,
      fontSize: 9, fontFace: "Consolas", color: C.gray, margin: 0,
    });
  });

  // Subscribe topics
  addCard(slide, 5.2, 3.1, 4.3, 2.2);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 3.1, w: 4.3, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Subscribe Topics", {
    x: 5.4, y: 3.2, w: 3.9, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });
  slide.addText([
    { text: "/cmd_vel\n", options: { fontFace: "Consolas", fontSize: 10, color: C.white, breakLine: true } },
    { text: "geometry_msgs/Twist\nСовместим со стандартной\nROS навигацией\n\n", options: { fontSize: 9, color: C.gray, breakLine: true } },
    { text: "/uavsim/ks0223/cmd_drive\n", options: { fontFace: "Consolas", fontSize: 10, color: C.white, breakLine: true } },
    { text: "std_msgs/Float32MultiArray\nПрямое PWM-управление", options: { fontSize: 9, color: C.gray } },
  ], {
    x: 5.4, y: 3.55, w: 3.9, h: 1.6,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.15,
  });

  slide.addNotes("ROS2 Bridge — ключевой компонент для sim-to-real.\n\nМост транслирует данные между HTTP JSON API симулятора и ROS2 топиками. Это позволяет использовать симулятор как виртуальный ROS2-робот.\n\nPublish: одометрия, камера (raw + compressed), ультразвук, датчик линии, спидометр, батарея.\n\nSubscribe: /cmd_vel (стандарт ROS навигации — Twist сообщения) и /cmd_drive (прямое PWM управление).\n\nДоступны Docker-образы для быстрого старта: make demo-up запускает ROS2 + bridge + rqt.");
}

// ====================================================================
// SLIDE 13: SIM-TO-REAL
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Sim-to-Real: от симуляции к реальному роботу");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // Two parallel paths
  // Left - Simulation
  addCard(slide, 0.5, 1.1, 4.3, 3.0);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 1.1, w: 4.3, h: 0.06,
    fill: { color: C.blue },
  });
  slide.addText("Симуляция (unity-sim)", {
    x: 0.7, y: 1.2, w: 3.9, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.blue, bold: true, margin: 0,
  });
  const simFeatures = [
    "Unity Runtime HTTP API",
    "Идеальная физика и сенсоры",
    "Ускоренное время симуляции",
    "Полная воспроизводимость (seed)",
    "Безопасные эксперименты",
    "Неограниченные итерации обучения",
  ];
  simFeatures.forEach((f, i) => {
    slide.addText(f, {
      x: 0.7, y: 1.65 + i * 0.37, w: 3.9, h: 0.3,
      fontSize: 10.5, fontFace: F.body, color: C.gray, margin: 0, bullet: true,
    });
  });

  // Right - Real robot
  addCard(slide, 5.2, 1.1, 4.3, 3.0);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 5.2, y: 1.1, w: 4.3, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Реальный робот (real-robot)", {
    x: 5.4, y: 1.2, w: 3.9, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });
  const realFeatures = [
    "TCP подключение к Raspberry Pi",
    "Реальные сенсоры с шумом",
    "UDP видеопоток с камеры",
    "Telemetry add-on (:8765)",
    "Та же ONNX модель, тот же backend",
    "Единый Web UI для управления",
  ];
  realFeatures.forEach((f, i) => {
    slide.addText(f, {
      x: 5.4, y: 1.65 + i * 0.37, w: 3.9, h: 0.3,
      fontSize: 10.5, fontFace: F.body, color: C.gray, margin: 0, bullet: true,
    });
  });

  // Bottom - key insight
  addCard(slide, 0.5, 4.4, 9.0, 0.9);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 4.4, w: 9.0, h: 0.06,
    fill: { color: C.accent },
  });
  slide.addText("Единый backend: переключение между unity-sim и real-robot через RuntimeProvider", {
    x: 0.7, y: 4.55, w: 8.6, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.accent, bold: true, margin: 0, align: "center",
  });
  slide.addText("Одна модель, один Web UI, один API — меняется только транспорт к runtime", {
    x: 0.7, y: 4.85, w: 8.6, h: 0.3,
    fontSize: 11, fontFace: F.body, color: C.gray, margin: 0, align: "center",
  });

  slide.addNotes("Sim-to-Real — архитектурное решение, а не просто перенос модели.\n\nBackend абстрагирует runtime через RuntimeProvider: UnityKs0223RuntimeProvider для симуляции, RealKs0223RuntimeProvider для реального робота.\n\nВ симуляции: HTTP API, идеальные условия, быстрое обучение.\nНа реальном роботе: TCP к Raspberry Pi (порт 5051), UDP видеопоток, реальные сенсоры с шумом.\n\nКлючевое: та же ONNX модель, тот же backend, тот же Web UI. Меняется только транспорт. Это минимизирует domain gap при трансфере.");
}

// ====================================================================
// SLIDE 14: EXPERIMENT RESULTS / KPI
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Экспериментальная оценка");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // KPI chart - bar chart showing episode metrics
  slide.addChart(pres.charts.BAR, [{
    name: "Шаги до завершения",
    labels: ["Ep.1", "Ep.2", "Ep.3", "Ep.4", "Ep.5", "Ep.6", "Ep.7", "Ep.8", "Ep.9", "Ep.10",
             "Ep.11", "Ep.12", "Ep.13", "Ep.14", "Ep.15", "Ep.16", "Ep.17", "Ep.18", "Ep.19", "Ep.20"],
    values: [203, 211, 199, 203, 204, 208, 203, 208, 208, 203, 207, 206, 202, 201, 198, 206, 204, 196, 200, 208],
  }], {
    x: 0.5, y: 1.1, w: 5.5, h: 2.8,
    barDir: "col",
    chartColors: [C.accent],
    chartArea: { fill: { color: C.bgCard }, roundedCorners: true },
    catAxisLabelColor: C.gray,
    valAxisLabelColor: C.gray,
    catAxisLabelFontSize: 7,
    valGridLine: { color: "30363D", size: 0.5 },
    catGridLine: { style: "none" },
    showLegend: false,
    showTitle: true,
    title: "Шаги до завершения по эпизодам",
    titleColor: C.white,
    titleFontSize: 11,
  });

  // Stats cards on the right - compact spacing
  const kpiStats = [
    { value: "20/20", label: "Эпизодов\nоценено", color: C.accent },
    { value: "203.9", label: "Средние\nшаги", color: C.blue },
    { value: "0%", label: "Success\nrate", color: C.orange },
    { value: "0.76 м", label: "Макс.\nотклонение", color: C.purple },
  ];

  kpiStats.forEach((s, i) => {
    const y = 1.1 + i * 0.7;
    addCard(slide, 6.4, y, 3.1, 0.6);
    slide.addText(s.value, {
      x: 6.5, y: y + 0.02, w: 1.4, h: 0.56,
      fontSize: 20, fontFace: F.title, color: s.color, bold: true, align: "center", valign: "middle", margin: 0,
    });
    slide.addText(s.label, {
      x: 7.9, y: y + 0.05, w: 1.5, h: 0.5,
      fontSize: 9, fontFace: F.body, color: C.gray, margin: 0, lineSpacingMultiple: 1.2,
    });
  });

  // Analysis section
  addCard(slide, 0.5, 4.15, 9.0, 1.15);
  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0.5, y: 4.15, w: 9.0, h: 0.06,
    fill: { color: C.orange },
  });
  slide.addText("Анализ: базовая модель", {
    x: 0.7, y: 4.25, w: 3.0, h: 0.3,
    fontSize: 13, fontFace: F.body, color: C.orange, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Baseline модель проходит прямой участок, но не справляется с поворотом (все 20 эпизодов: out_of_bounds).\n", options: { color: C.gray, fontSize: 10, breakLine: true } },
    { text: "Стабильное поведение (203.9 шагов) подтверждает воспроизводимость платформы. ", options: { color: C.white, fontSize: 10 } },
    { text: "Pipeline работает E2E.", options: { color: C.accent, fontSize: 10, bold: true } },
  ], {
    x: 0.7, y: 4.55, w: 8.6, h: 0.65,
    fontFace: F.body, margin: 0, lineSpacingMultiple: 1.2,
  });

  slide.addNotes("Экспериментальная оценка проводилась на 20 эпизодах с baseline моделью.\n\nРезультаты:\n- 20 из 20 эпизодов завершились out_of_bounds\n- Среднее количество шагов: 203.9 (из макс. 220)\n- Максимальное отклонение от центральной линии: 0.76 м\n- Success rate: 0%\n\nАнализ: модель уверенно проходит прямой участок, но не справляется с первым поворотом. Все эпизоды завершаются в одной точке (routeIndex=2, waypoint 3 из 5).\n\nВажно: стабильное поведение (малый разброс шагов) подтверждает воспроизводимость платформы. Pipeline работает end-to-end: обучение → загрузка → активация → автопилот → оценка.");
}

// ====================================================================
// SLIDE 15: PRACTICAL SIGNIFICANCE
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bg };
  addTitleBar(slide, "Практическая значимость");
  addSlideNumber(slide, slideNum, TOTAL_SLIDES);

  // 3 audience cards
  const audiences = [
    {
      title: "Исследователи",
      desc: "Платформа для экспериментов\nс RL-алгоритмами в управлении\nроботами. Воспроизводимость,\nсценарии, KPI-оценка.",
      color: C.blue,
    },
    {
      title: "Разработчики",
      desc: "Расширяемая архитектура\nс плагинами. Plugin SDK,\nшаблоны, CLI для быстрого\nстарта новых роботов и трасс.",
      color: C.accent,
    },
    {
      title: "Образование",
      desc: "Безопасная среда для обучения\nробототехнике. Web UI оператора,\nROS2 интеграция, готовые\nсценарии демонстрации.",
      color: C.purple,
    },
  ];

  audiences.forEach((a, i) => {
    const x = 0.5 + i * 3.1;
    addCard(slide, x, 1.1, 2.8, 2.5);
    slide.addShape(pres.shapes.RECTANGLE, {
      x: x, y: 1.1, w: 2.8, h: 0.06,
      fill: { color: a.color },
    });
    slide.addText(a.title, {
      x: x + 0.15, y: 1.25, w: 2.5, h: 0.35,
      fontSize: 16, fontFace: F.body, color: a.color, bold: true, align: "center", margin: 0,
    });
    slide.addText(a.desc, {
      x: x + 0.15, y: 1.7, w: 2.5, h: 1.7,
      fontSize: 11, fontFace: F.body, color: C.gray, align: "center", margin: 0, lineSpacingMultiple: 1.3,
    });
  });

  // Bottom - key metrics
  addCard(slide, 0.5, 3.9, 9.0, 1.4);
  slide.addText("Ключевые показатели платформы", {
    x: 0.7, y: 4.0, w: 8.6, h: 0.3,
    fontSize: 14, fontFace: F.body, color: C.white, bold: true, margin: 0, align: "center",
  });

  const metrics = [
    { value: "7", label: "роботов" },
    { value: "4", label: "трассы" },
    { value: "6", label: "ROS2 топиков" },
    { value: "E2E", label: "pipeline" },
    { value: "2", label: "runtime режима" },
  ];
  metrics.forEach((m, i) => {
    const x = 0.7 + i * 1.75;
    slide.addText(m.value, {
      x: x, y: 4.35, w: 1.5, h: 0.4,
      fontSize: 24, fontFace: F.title, color: C.accent, bold: true, align: "center", margin: 0,
    });
    slide.addText(m.label, {
      x: x, y: 4.75, w: 1.5, h: 0.3,
      fontSize: 10, fontFace: F.body, color: C.gray, align: "center", margin: 0,
    });
  });

  slide.addNotes("Практическая значимость для трёх аудиторий:\n\n1. Исследователи — получают платформу для экспериментов с RL в робототехнике. Воспроизводимость через сценарии и seed, KPI-оценка.\n\n2. Разработчики — расширяемая архитектура с Plugin SDK. Можно добавить нового робота или трассу без модификации ядра.\n\n3. Образование — безопасная среда для обучения робототехнике. Web UI оператора делает платформу доступной для студентов.\n\nКлючевые цифры: 7 роботов, 4 трассы, 6 ROS2 топиков, полный E2E pipeline, 2 runtime режима (симуляция и реальный робот).");
}

// ====================================================================
// SLIDE 16: CONCLUSION
// ====================================================================
slideNum++;
{
  const slide = pres.addSlide();
  slide.background = { color: C.bgTitle };

  slide.addShape(pres.shapes.RECTANGLE, {
    x: 0, y: 0, w: 10, h: 0.08,
    fill: { color: C.accent },
  });

  slide.addText("Заключение", {
    x: 0.5, y: 0.3, w: 9, h: 0.55,
    fontSize: 30, fontFace: F.title, color: C.title, bold: true, margin: 0, align: "center",
  });

  // Results summary
  const results = [
    { check: true, text: "Спроектирована и реализована модульная архитектура платформы симуляции (4 компонента)" },
    { check: true, text: "Реализована физическая модель робота KS0223 с 5 типами сенсоров" },
    { check: true, text: "Разработана система плагинов с SDK, шаблонами и CLI" },
    { check: true, text: "Реализован полный model lifecycle: обучение, загрузка, активация, запуск, оценка" },
    { check: true, text: "Создан ROS2 Bridge для sim-to-real трансфера (6 publish + 2 subscribe топика)" },
    { check: true, text: "Проведена экспериментальная оценка на 20 эпизодах, подтверждена воспроизводимость" },
  ];

  results.forEach((r, i) => {
    const y = 1.15 + i * 0.55;
    slide.addShape(pres.shapes.RECTANGLE, {
      x: 0.8, y: y, w: 8.4, h: 0.45,
      fill: { color: C.bgCard },
    });
    slide.addShape(pres.shapes.RECTANGLE, {
      x: 0.8, y: y, w: 0.06, h: 0.45,
      fill: { color: C.accent },
    });
    slide.addText(r.text, {
      x: 1.1, y: y + 0.02, w: 7.9, h: 0.4,
      fontSize: 11.5, fontFace: F.body, color: C.white, margin: 0, valign: "middle",
    });
  });

  // Future work
  slide.addText("Направления развития", {
    x: 0.8, y: 4.55, w: 8.4, h: 0.35,
    fontSize: 14, fontFace: F.body, color: C.accent, bold: true, margin: 0,
  });
  slide.addText([
    { text: "Domain Randomization для улучшения sim-to-real трансфера", options: { bullet: true, color: C.gray, fontSize: 10, breakLine: true } },
    { text: "Multi-agent сценарии для обучения взаимодействию роботов", options: { bullet: true, color: C.gray, fontSize: 10 } },
  ], {
    x: 0.8, y: 4.9, w: 8.4, h: 0.55,
    fontFace: F.body, margin: 0,
  });

  slide.addNotes("В заключение:\n\nВсе 6 задач выполнены:\n1. Модульная архитектура из 4 компонентов\n2. Физическая модель KS0223 с 5 сенсорами\n3. Система плагинов с SDK\n4. Полный model lifecycle\n5. ROS2 Bridge\n6. Экспериментальная оценка\n\nНаправления развития:\n- Domain Randomization для уменьшения domain gap\n- Multi-agent сценарии\n\nСпасибо за внимание! Готов ответить на вопросы.");
}

// ========== GENERATE ==========
const outputPath = path.join(BASE, "output/defense-presentation.pptx");
pres.writeFile({ fileName: outputPath }).then(() => {
  console.log(`Presentation saved to: ${outputPath}`);
}).catch(err => {
  console.error("Error:", err);
});
