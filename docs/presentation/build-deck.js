// Builds the Hazem storyboard deck (Arabic, right-to-left).
const pptxgen = require("pptxgenjs");
const path = require("path");
const { applyTheme } = require(process.env.PPTX_SKILL + "/scripts/apply_theme.js");

const OUT = process.argv[2];

const THEME = {
  name: "Hazem Storyboard",
  headFontFace: "Arial",
  bodyFontFace: "Arial",
  colors: {
    dk1: "13201D", lt1: "FFFFFF", dk2: "0E6A59", lt2: "EEF2F1",
    accent1: "0E6A59", accent2: "9A7128", accent3: "D8EEE6", accent4: "1D7A4E",
    accent5: "9A6A12", accent6: "56665F", hlink: "0E6A59", folHlink: "56665F",
  },
};
const HEX = THEME.colors;

const pres = new pptxgen();
pres.layout = "LAYOUT_WIDE"; // 13.33 x 7.5
pres.rtlMode = true;
pres.title = "ستوري بورد حازم";
pres.author = "مشروع حازم";
pres.theme = { headFontFace: THEME.headFontFace, bodyFontFace: THEME.bodyFontFace };
const C = pres.SchemeColor;

const W = 13.333, H = 7.5, M = 0.6;
const AR = { rtlMode: true, lang: "ar-EG", align: "right" };

// ---------- Layouts ----------
pres.defineSlideMaster({
  title: "COVER",
  background: { color: C.accent1 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: M, y: 2.2, w: W - 2 * M, h: 1.6,
      fontSize: 72, bold: true, color: C.background1, align: "right", valign: "bottom", rtlMode: true, margin: 0 }, text: "" } },
    { placeholder: { options: { name: "body", type: "body", x: M, y: 3.9, w: W - 2 * M, h: 1.6,
      fontSize: 24, color: C.background1, align: "right", valign: "top", rtlMode: true, margin: 0 }, text: "" } },
  ],
});
pres.defineSlideMaster({
  title: "CONTENT",
  background: { color: C.background1 },
  margin: [0.5, 0.6, 0.6, 0.6],
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: M, y: 0.4, w: W - 2 * M, h: 0.9,
      fontSize: 36, bold: true, color: C.text1, align: "right", valign: "middle", rtlMode: true, margin: 0 }, text: "" } },
  ],
  slideNumber: { x: 0.5, y: 6.95, w: 0.6, h: 0.35, fontSize: 10, color: C.accent6, align: "left" },
});
pres.defineSlideMaster({
  title: "SCENE",
  background: { color: C.background2 },
  objects: [
    { placeholder: { options: { name: "title", type: "title", x: 6.1, y: 0.4, w: W - M - 6.1, h: 0.9,
      fontSize: 36, bold: true, color: C.text1, align: "right", valign: "middle", rtlMode: true, margin: 0 }, text: "" } },
  ],
  slideNumber: { x: 0.5, y: 6.95, w: 0.6, h: 0.35, fontSize: 10, color: C.accent6, align: "left" },
});

// ---------- Helpers ----------
// A right-to-left mark after a number keeps "3 أرقام" from rendering as "3أرقام".
function rtlFix(t) { return t.replace(/(\d[\d,.:/]*)(?=\s)/g, "$1‏"); }
function fixRuns(text) {
  return typeof text === "string" ? rtlFix(text) : text.map((r) => Object.assign({}, r, { text: rtlFix(r.text) }));
}
function txt(slide, text, opts) {
  const o = Object.assign({ isTextBox: true, margin: 0, fontSize: 16, color: C.text1 }, AR, opts);
  slide.addText(o.rtlMode === false ? text : fixRuns(text), o);
}
function ph(slide, text, name) { slide.addText(fixRuns(text), { placeholder: name }); }

function estLines(text, widthIn, fontPt) {
  const charsPerLine = Math.max(8, Math.floor((widthIn * 72) / (fontPt * 0.43)));
  return text.split("\n").reduce((n, line) => n + Math.max(1, Math.ceil(line.length / charsPerLine)), 0);
}

// A WhatsApp-like phone screen. messages: {who: client|hazem|note|file|voice, text, time}
function phone(slide, x, y, w, h, messages, name) {
  slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h, rectRadius: 0.25,
    fill: { color: C.background2 }, line: { color: "D3DCD8", width: 1 }, objectName: name + " phone" });
  slide.addShape(pres.shapes.RECTANGLE, { x: x + 0.02, y: y + 0.12, w: w - 0.04, h: 0.62,
    fill: { color: C.accent1 }, line: { type: "none" }, objectName: name + " phone header" });
  slide.addShape(pres.shapes.OVAL, { x: x + w - 0.62, y: y + 0.24, w: 0.38, h: 0.38,
    fill: { color: C.background1, transparency: 75 }, line: { type: "none" } });
  txt(slide, "ح", { x: x + w - 0.62, y: y + 0.24, w: 0.38, h: 0.38, fontSize: 14, bold: true,
    color: C.background1, align: "center", valign: "middle" });
  txt(slide, [
    { text: "مكتب الأستاذ سعد فتحي سعد", options: { fontSize: 13, bold: true, color: C.background1, breakLine: true } },
    { text: "حازم · المساعد الرقمي", options: { fontSize: 10, color: C.background1 } },
  ], { x: x + 0.25, y: y + 0.16, w: w - 1.0, h: 0.56, valign: "middle" });

  let cy = y + 0.95;
  const pad = 0.22;
  for (const m of messages) {
    if (m.who === "note") {
      const nw = Math.min(w - 2 * pad, 0.14 * m.text.length + 0.5);
      slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: x + (w - nw) / 2, y: cy, w: nw, h: 0.34, rectRadius: 0.17,
        fill: { color: C.background1 }, line: { color: "B9C6C1", width: 0.75, dashType: "dash" } });
      txt(slide, m.text, { x: x + (w - nw) / 2, y: cy, w: nw, h: 0.34, fontSize: 11, color: C.accent6, align: "center", valign: "middle" });
      cy += 0.46;
      continue;
    }
    const bw = (m.who === "file" || m.who === "voice") ? 2.4 : (w - 2 * pad) * 0.86;
    const fs = 14;
    const lines = (m.who === "file" || m.who === "voice") ? 1 : estLines(m.text, bw - 0.3, fs);
    const bh = lines * fs * 1.45 / 72 + 0.42;
    const isClient = m.who !== "hazem";
    const bx = isClient ? x + w - pad - bw : x + pad;
    slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: bx, y: cy, w: bw, h: bh, rectRadius: 0.12,
      fill: { color: isClient ? C.background1 : C.accent3 }, line: { type: "none" },
      shadow: { type: "outer", color: "000000", opacity: 0.08, blur: 2, offset: 1, angle: 90 } });
    if (m.who === "file") {
      slide.addShape(pres.shapes.RECTANGLE, { x: bx + bw - 0.55, y: cy + 0.1, w: 0.36, h: bh - 0.2,
        fill: { color: "D3DCD8" }, line: { type: "none" } });
      txt(slide, "JPG", { x: bx + bw - 0.55, y: cy + 0.1, w: 0.36, h: bh - 0.2, fontSize: 8, color: C.accent6, align: "center", valign: "middle", rtlMode: false });
      txt(slide, m.text, { x: bx + 0.15, y: cy + 0.06, w: bw - 0.8, h: bh - 0.3, fontSize: fs, valign: "middle" });
    } else if (m.who === "voice") {
      slide.addShape(pres.shapes.RIGHT_TRIANGLE, { x: bx + bw - 0.4, y: cy + 0.17, w: 0.16, h: 0.2, rotate: 0,
        fill: { color: C.accent1 }, line: { type: "none" }, flipH: true });
      for (let i = 0; i < 18; i++) {
        const bar = 0.06 + ((i * 37) % 7) * 0.025;
        slide.addShape(pres.shapes.RECTANGLE, { x: bx + bw - 0.6 - i * 0.085, y: cy + 0.27 - bar / 2, w: 0.035, h: bar,
          fill: { color: C.accent6 }, line: { type: "none" } });
      }
      txt(slide, m.text, { x: bx + 0.12, y: cy + 0.12, w: 0.5, h: 0.3, fontSize: 10, color: C.accent6, align: "left", rtlMode: false });
    } else {
      txt(slide, m.text, { x: bx + 0.15, y: cy + 0.08, w: bw - 0.3, h: bh - 0.32, fontSize: fs, valign: "top" });
    }
    if (m.time) {
      txt(slide, m.time, { x: bx + 0.12, y: cy + bh - 0.26, w: 1.0, h: 0.2, fontSize: 8, color: C.accent6, align: "left", rtlMode: false });
    }
    cy += bh + 0.14;
  }
  return cy;
}

function chip(slide, text, x, y, opts = {}) {
  const w = opts.w || 0.95;
  slide.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w, h: 0.34, rectRadius: 0.17,
    fill: { color: opts.fill || C.text1 }, line: { color: opts.line || C.accent3, width: 1 } });
  txt(slide, text, { x, y, w, h: 0.34, fontSize: 11, bold: true, color: opts.color || C.accent3, align: "center", valign: "middle", rtlMode: false });
}

// ---------- Content ----------
const scenes = [
  {
    n: "01", title: "العميل يكتب", clock: "الأحد 11:52 م", layer: "n8n · طبقة الواتساب",
    chat: [
      { who: "client", text: "السلام عليكم", time: "11:52:01 م" },
      { who: "client", text: "عندي مشكلة مع المطور في التجمع", time: "11:52:04 م" },
      { who: "client", text: "محتاج أفهم أعمل إيه", time: "11:52:07 م" },
    ],
    back: "Meta بتبعت كل رسالة لوحدها. n8n بيتأكد إنها جاية من Meta فعلًا، ويحفظها مرة واحدة، ويستنى 8 ثواني من غير رسايل جديدة. بعدها الـ 3 رسايل بيبقوا دور واحد ورد واحد.",
    rules: ["8 ثواني"],
    notes: "العميل بيبعت تلات رسايل ورا بعض الساعة 12 إلا 8 بالليل. حازم ما بيردش على كل رسالة لوحدها. بيستنى 8 ثواني لحد ما العميل يخلّص كلامه، وبعدين يرد مرة واحدة.",
  },
  {
    n: "02", title: "الترحيب والاسم", clock: "الأحد 11:52 م", layer: "العقل",
    chat: [
      { who: "hazem", text: "أهلًا بحضرتك في مكتب الأستاذ سعد فتحي سعد للمحاماة، معاك حازم. ممكن اسم حضرتك رباعي؟", time: "11:52 م" },
      { who: "client", text: "أحمد محمود عبد الله السيد", time: "11:53 م" },
    ],
    back: "الرقم بيتراجع على القوائم بالترتيب: الممنوعين، وقائمة الأستاذ، والموكلين الحاليين. لو موكل حالي، حازم يحوّله للموظف المسؤول من غير ما يقول أي معلومة عن قضيته.",
    rules: ["B-01", "B-14"],
    notes: "أول حاجة حازم بيعملها إنه يرحّب ويطلب الاسم رباعي. وفي نفس اللحظة النظام بيشوف الرقم ده: عميل جديد، ولا موكل حالي، ولا رقم ممنوع. الاسم ده مثال.",
  },
  {
    n: "03", title: "عرض الاستشارة", clock: "الأحد 11:53 م", layer: "AI · فهم النية",
    chat: [
      { who: "hazem", text: "الأستاذ سعد هيسمع موضوع حضرتك بنفسه. متاح استشارة بالتليفون أو الفيديو، ربع ساعة بـ500 جنيه، أو مقابلة في المكتب، نص ساعة بـ1,000 جنيه. تحب أنهي واحدة؟", time: "11:53 م" },
      { who: "client", text: "مقابلة في المكتب", time: "11:54 م" },
    ],
    back: "الذكاء الاصطناعي بيفهم نية العميل بس، زي «عايز يحجز». أما الرد نفسه فبيتاخد من نصوص معتمدة من المكتب. حازم ما بيألّفش سعر ولا رأي.",
    rules: ["T-07"],
    notes: "هنا حازم بيعرض الاستشارة بالأسعار الرسمية. النقطة المهمة: الذكاء الاصطناعي ما بيكتبش الرد، هو بيفهم العميل عاوز إيه بس، والرد نص المكتب وافق عليه.",
  },
  {
    n: "04", title: "بيانات الدفع", clock: "الأحد 11:54 م", layer: "قواعد الحجز",
    chat: [
      { who: "hazem", text: "مقابلة في المكتب بـ1,000 جنيه. التحويل على InstaPay أو محفظة كاش. بعد التحويل ابعتلي صورة الإيصال هنا، والمكتب هيأكد الميعاد مع حضرتك. الاسترداد كامل لو الإلغاء خلال 24 ساعة من الدفع وقبل بداية الخدمة.", time: "11:54 م" },
    ],
    back: "مفيش أي ميعاد بيتحجز قبل ما الدفع يتراجع، عشان تقويم الأستاذ ما يتملاش بحجوزات وهمية.",
    rules: ["B-08"],
    notes: "الدفع الأول، وبعدين الميعاد. ده قرار المكتب، عشان ما يبقاش فيه مواعيد محجوزة لناس مش جادين.",
  },
  {
    n: "05", title: "الإيصال", clock: "الأحد 11:58 م", layer: "تخزين الملفات",
    chat: [
      { who: "file", text: "صورة الإيصال" },
      { who: "hazem", text: "استلمنا الإيصال، وهيتم مراجعته وتأكيد الحجز بكرة الساعة 10 الصبح. مواعيد العمل الرسمية من 10 الصبح لحد 12 بالليل.", time: "11:58 م" },
    ],
    back: "الصورة بتتنزل من Meta على طول، وتتحفظ في مخزن الملفات الحساسة. صور الإيصالات والبطاقات عمرها ما بتروح لأي ذكاء اصطناعي. وبعد الساعة 11 بالليل الوعد بيبقى «بكرة الساعة 10».",
    rules: ["T-09", "B-06"],
    notes: "العميل بعت صورة الإيصال بالليل. حازم بيقوله بوضوح إن المراجعة بكرة الساعة 10، مش «خلال ساعة»، لأن المكتب قافل. والصورة متخزنة في مكان آمن، ومحدش بيبعتها لأي ذكاء اصطناعي.",
  },
  {
    n: "06", title: "الموظف يأكد", clock: "الاثنين 10:20 ص", layer: "الموظف",
    chat: [
      { who: "note", text: "الموظف راجع الإيصال وحدد الميعاد" },
      { who: "hazem", text: "تم تأكيد ميعاد حضرتك يوم الثلاثاء الساعة 6 مساءً ✅. التفاصيل: مقابلة في المكتب. لو محتاج أي حاجة ابعتلنا هنا.", time: "10:20 ص" },
    ],
    back: "الإيصال بيفتح مهمة مراجعة للموظف في لوحة المكتب. الموظف هو اللي بيقبل أو يرفض، وهو اللي بيحدد الميعاد. حازم بيبلّغ القرار بس.",
    rules: ["B-05"],
    notes: "الصبح الموظف بيلاقي الإيصال مستنيه في اللوحة. بيراجعه ويحدد الميعاد، وحازم بيبعت التأكيد للعميل. القرار هنا بشري.",
  },
  {
    n: "07", title: "التذكير", clock: "الاثنين 6:00 م", layer: "قوالب Meta",
    chat: [
      { who: "hazem", text: "تذكير بميعاد حضرتك مع الأستاذ سعد بكرة الثلاثاء الساعة 6 مساءً. نوع الاستشارة: مقابلة في المكتب. التفاصيل: العنوان واللوكيشن. لو محتاج تغيّر الميعاد ابعتلنا هنا.", time: "6:00 م" },
      { who: "note", text: "وتذكير تاني قبل الميعاد بساعتين" },
    ],
    back: "بعد 24 ساعة من آخر رسالة للعميل، واتساب ما بيسمحش برسالة عادية. التذكير بيتبعت كقالب Meta وافقت عليه مسبقًا.",
    rules: ["M-02", "M-03"],
    notes: "تذكيرين: قبل الميعاد بيوم وقبله بساعتين. وده بيقلل إن العميل ينسى أو ما يجيش. والرسايل دي قوالب Meta موافقة عليها، لأن واتساب له قواعد صارمة.",
  },
  {
    n: "08", title: "قدام الأستاذ", clock: "الثلاثاء 6:00 م", layer: "الأستاذ",
    chat: [
      { who: "note", text: "كارت العميل عند الأستاذ" },
      { who: "client", text: "الاسم: أحمد محمود عبد الله السيد\nالخدمة: مقابلة في المكتب · مدفوع ✅\nكلامه زي ما كتبه، وتفريغ أي فويس، والورق اللي بعته\nجاي منين: إعلان واتساب" },
    ],
    back: "الأستاذ بيدخل الاستشارة وهو عارف الموضوع من غير ما يقرا المحادثة كلها. والرأي القانوني كله من عنده.",
    rules: [],
    notes: "آخر لقطة: الأستاذ قدامه كارت فيه كل حاجة عن العميل. وقت الاستشارة كله بيروح في الموضوع نفسه، مش في «احكيلي حصل إيه».",
  },
];

// ---------- Slides ----------
pres.addSection({ title: "الافتتاحية" });
let s = pres.addSlide({ masterName: "COVER", sectionTitle: "الافتتاحية" });
ph(s, "حازم", "title");
ph(s, [
  { text: "مساعد واتساب لمكتب الأستاذ سعد فتحي سعد للمحاماة", options: { breakLine: true } },
  { text: "ستوري بورد المشروع · أكتوبر 2026", options: { fontSize: 16 } },
], "body");
s.addNotes("النهارده هحكيلكم عن حازم: مساعد واتساب لمكتب الأستاذ سعد. هنمشي مع عميل حقيقي من أول رسالة بالليل لحد ما يقعد قدام الأستاذ، وبعدها نشوف النظام من جوه وإيه المطلوب من المكتب.");

s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الافتتاحية" });
ph(s, "المكتب بيخسر عملاء بالليل", "title");
txt(s, "أغلب الناس بتكتب للمحامي وقت المشكلة، مش وقت مواعيد المكتب.", { x: M, y: 1.3, w: W - 2 * M, h: 0.5, fontSize: 18, color: C.accent6 });
[
  ["رسايل من غير رد", "عميل يكتب الساعة 2 الفجر، ولما الموظف يرد الصبح يكون كلّم مكتب تاني."],
  ["نفس الأسئلة كل يوم", "الاستشارة بكام؟ المكتب فين؟ أدفع إزاي؟ الموظف بيضيّع وقته في ردود متكررة."],
  ["ورق وإيصالات متلخبطة", "صور بطاقات وإيصالات وعقود في محادثات متفرقة، من غير مراجعة ولا سجل."],
].forEach(([h, p], i) => {
  const cw = (W - 2 * M - 2 * 0.4) / 3;
  const x = W - M - cw - i * (cw + 0.4);
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: 2.3, w: cw, h: 3.6, rectRadius: 0.12, fill: { color: C.background2 }, line: { type: "none" }, objectName: "problem " + (i + 1) });
  s.addShape(pres.shapes.OVAL, { x: x + cw - 0.95, y: 2.6, w: 0.6, h: 0.6, fill: { color: C.accent1 }, line: { type: "none" } });
  txt(s, String(i + 1), { x: x + cw - 0.95, y: 2.6, w: 0.6, h: 0.6, fontSize: 20, bold: true, color: C.background1, align: "center", valign: "middle", rtlMode: false });
  txt(s, h, { x: x + 0.35, y: 3.4, w: cw - 0.7, h: 0.6, fontSize: 22, bold: true });
  txt(s, p, { x: x + 0.35, y: 4.05, w: cw - 0.7, h: 1.6, fontSize: 16, color: C.accent6, valign: "top" });
});
s.addNotes("التلات مشاكل دي اللي بنحلها: رسايل بالليل محدش بيرد عليها، وأسئلة متكررة بتاخد وقت الموظفين، وورق مهم متبعتر في الشات.");

s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الافتتاحية" });
ph(s, "الفكرة في 3 أرقام", "title");
[
  ["24 ساعة", "حازم بيستقبل الرسايل طول اليوم"],
  ["14 ساعة", "مواعيد المكتب، من 10 الصبح لـ 12 بالليل"],
  ["صفر", "آراء قانونية من الآلة. القرار للمكتب"],
].forEach(([big, small], i) => {
  const cw = (W - 2 * M) / 3;
  const x = W - M - cw - i * cw;
  txt(s, big, { x, y: 2.4, w: cw - 0.3, h: 1.4, fontSize: 60, bold: true, color: C.accent1, valign: "bottom" });
  txt(s, small, { x, y: 3.9, w: cw - 0.3, h: 0.9, fontSize: 18, color: C.accent6, valign: "top" });
});
txt(s, "العميل يكتب في أي وقت، وحازم يستقبله ويرتّب طلبه ويمشّيه لحد ميعاد مع الأستاذ.", { x: M, y: 5.5, w: W - 2 * M, h: 0.6, fontSize: 20, italic: true });
s.addNotes("حازم شغال 24 ساعة، بس بيحترم مواعيد المكتب: لو العميل كتب بالليل بيقوله إن المراجعة الصبح. والأهم: صفر آراء قانونية من الآلة.");

pres.addSection({ title: "رحلة العميل" });
s = pres.addSlide({ masterName: "COVER", sectionTitle: "رحلة العميل" });
ph(s, "رحلة عميل جديد", "title");
ph(s, "8 لقطات، من أول رسالة الساعة 12 إلا 8 بالليل لحد ما يقعد قدام الأستاذ. الاسم والمواعيد أمثلة.", "body");
s.addNotes("دلوقتي هنمشي مع عميل اسمه أحمد، والاسم مثال. في كل لقطة هتشوفوا الشاشة اللي العميل شايفها، وجنبها اللي بيحصل في النظام في نفس اللحظة.");

for (const sc of scenes) {
  s = pres.addSlide({ masterName: "SCENE", sectionTitle: "رحلة العميل" });
  ph(s, [
    { text: sc.n + "  ", options: { color: C.accent2 } },
    { text: sc.title },
  ], "title");
  txt(s, sc.clock, { x: 6.1, y: 1.3, w: W - M - 6.1, h: 0.4, fontSize: 14, color: C.accent6 });

  const pw = 5.0, phh = 6.55;
  phone(s, M, 0.45, pw, phh, sc.chat, "scene " + sc.n);

  const bx = 6.1, by = 2.0, bw = W - M - 6.1, bh = 4.4;
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: bx, y: by, w: bw, h: bh, rectRadius: 0.14,
    fill: { color: C.text1 }, line: { type: "none" }, objectName: "behind the scenes " + sc.n });
  txt(s, "ورا الكواليس · " + sc.layer, { x: bx + 0.4, y: by + 0.35, w: bw - 0.8, h: 0.45, fontSize: 14, bold: true, color: C.accent3 });
  txt(s, sc.back, { x: bx + 0.4, y: by + 0.95, w: bw - 0.8, h: bh - 1.9, fontSize: 20, color: C.background1, valign: "top", lineSpacingMultiple: 1.15 });
  sc.rules.forEach((r, i) => chip(s, r, bx + bw - 0.4 - 1.05 - i * 1.2, by + bh - 0.75, { w: 1.05 }));
  s.addNotes(sc.notes);
}

pres.addSection({ title: "الحالات والنظام" });
s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الحالات والنظام" });
ph(s, "الحالات اللي بتحصل كل يوم", "title");
[
  ["فويس بدل كتابة", "(رسالة صوتية 0:42)", "الفويس بيتفرّغ لنص بالعربي، والتسجيل الأصلي بيتحفظ."],
  ["«مستعجل»", "استلمنا طلبك العاجل وبلغنا الأستاذ.", "تنبيه فوري للأستاذ وحده، وهو يقبل أو يرفض. العاجلة بالتليفون بـ1,000 جنيه."],
  ["موكل حالي", "رسالتك وصلت للمكتب، وهيرد عليك المسؤول.", "حازم ما بيأكدش ولا بينفي إن حد موكل، ولا بيقول أي معلومة عن قضية."],
  ["الموظف دخل المحادثة", "(حازم بيسكت)", "أول ما موظف يرد بنفسه، حازم يقف لحد ما الموظف يرجّعها."],
  ["زواج أو طلاق عرفي", "سن الزوج كام، وسن الزوجة كام؟", "سؤال واحد في كل رسالة وبترتيب ثابت، والأستاذ بيراجع قبل الميعاد."],
  ["ملف كبير أو غريب", "(أكبر من 50 ميجا، أو ملف تنفيذي)", "الملف ما بيتنزلش، والسبب بيتسجل، والرد من النصوص المعتمدة."],
].forEach(([h, say, p], i) => {
  const cols = 3, gap = 0.35;
  const cw = (W - 2 * M - (cols - 1) * gap) / cols, ch = 2.55;
  const col = i % cols, row = Math.floor(i / cols);
  const x = W - M - cw - col * (cw + gap), y = 1.5 + row * (ch + 0.3);
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w: cw, h: ch, rectRadius: 0.12, fill: { color: C.background2 }, line: { type: "none" }, objectName: "side case " + (i + 1) });
  txt(s, h, { x: x + 0.3, y: y + 0.22, w: cw - 0.6, h: 0.45, fontSize: 18, bold: true });
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x: x + 0.3, y: y + 0.75, w: cw - 0.6, h: 0.62, rectRadius: 0.1, fill: { color: C.accent3 }, line: { type: "none" } });
  txt(s, say, { x: x + 0.45, y: y + 0.75, w: cw - 0.9, h: 0.62, fontSize: 13, valign: "middle" });
  txt(s, p, { x: x + 0.3, y: y + 1.5, w: cw - 0.6, h: 0.95, fontSize: 14, color: C.accent6, valign: "top" });
});
s.addNotes("مش كل العملاء بيمشوا الطريق العادي. دي ست حالات بتحصل كل يوم، وكل واحدة ليها قاعدة واضحة.");

s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الحالات والنظام" });
ph(s, "جزء بيتكلم مع واتساب، وجزء بيفكّر", "title");
const nodes = [
  { t: "العميل", tech: "WhatsApp", items: ["نص، وفويس، وصور، ومستندات"], kind: "plain" },
  { t: "طبقة الواتساب", tech: "n8n", items: ["يتأكد إن الرسالة جاية من واتساب فعلًا", "يمنع التكرار ويجمع الـ 8 ثواني", "ينزّل الميديا ويفرّغ الفويس", "مفيش فيه أي قرار"], kind: "core" },
  { t: "العقل", tech: "ASP.NET Core", items: ["يفهم نية العميل (نص بس)", "يمشّي مراحل الحجز والدفع", "يختار الرد المعتمد"], kind: "brain" },
  { t: "المكتب", tech: "الموظف والأستاذ", items: ["الموظف يراجع ويحدد", "الأستاذ يستشير ويقرر"], kind: "plain" },
];
const links = ["Meta Cloud API", "API داخلي بتوكن", "مهام وتنبيهات"];
const nw = 2.3, gapX = (W - 2 * M - 4 * nw) / 3, ny = 1.55, nh = 3.0;
nodes.forEach((n, i) => {
  const x = W - M - nw - i * (nw + gapX);
  const fill = n.kind === "core" ? C.accent1 : n.kind === "brain" ? C.text1 : C.background2;
  const fg = n.kind === "plain" ? C.text1 : C.background1;
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y: ny, w: nw, h: nh, rectRadius: 0.12, fill: { color: fill }, line: { type: "none" }, objectName: "architecture " + n.t });
  txt(s, n.t, { x: x + 0.25, y: ny + 0.2, w: nw - 0.5, h: 0.45, fontSize: 20, bold: true, color: fg });
  txt(s, n.tech, { x: x + 0.25, y: ny + 0.65, w: nw - 0.5, h: 0.3, fontSize: 12, color: n.kind === "plain" ? C.accent2 : C.accent3, rtlMode: /[؀-ۿ]/.test(n.tech), align: "right" });
  txt(s, n.items.map((it, k) => ({ text: "• " + it, options: { breakLine: k < n.items.length - 1 } })),
    { x: x + 0.2, y: ny + 1.05, w: nw - 0.4, h: nh - 1.2, fontSize: 13, color: fg, valign: "top", paraSpaceAfter: 4 });
  if (i < 3) {
    const ax = x - gapX + 0.08;
    s.addShape(pres.shapes.LINE, { x: ax, y: ny + nh / 2, w: gapX - 0.16, h: 0, line: { color: HEX.accent6, width: 1.5, beginArrowType: "triangle" } });
    txt(s, links[i], { x: ax, y: ny + nh / 2 + 0.12, w: gapX - 0.16, h: 0.6, fontSize: 10, color: C.accent6, align: "center", valign: "top" });
  }
});
[
  ["قاعدة البيانات", "PostgreSQL", "جداول الواتساب لطبقة الواتساب بس، وجداول البيزنس للعقل بس"],
  ["مخزن الملفات", "Cloudflare R2", "مخزن للملفات الحساسة لوحده، والعقل بياخد مراجع بس"],
  ["الذكاء الاصطناعي", "OpenAI", "تفريغ الفويس وفهم النص. صفر صور أو مستندات"],
].forEach(([t, tech, p], i) => {
  const cw = (W - 2 * M - 2 * 0.4) / 3;
  const x = W - M - cw - i * (cw + 0.4), y = 4.9;
  s.addShape(pres.shapes.ROUNDED_RECTANGLE, { x, y, w: cw, h: 1.55, rectRadius: 0.12, fill: { color: C.background1 }, line: { color: "B9C6C1", width: 1, dashType: "dash" }, objectName: "store " + t });
  txt(s, t, { x: x + 0.25, y: y + 0.12, w: cw - 0.5, h: 0.36, fontSize: 16, bold: true });
  txt(s, tech, { x: x + 0.25, y: y + 0.48, w: cw - 0.5, h: 0.26, fontSize: 11, color: C.accent2, rtlMode: false, align: "right" });
  txt(s, p, { x: x + 0.25, y: y + 0.78, w: cw - 0.5, h: 0.7, fontSize: 13, color: C.accent6, valign: "top" });
});
s.addNotes("النظام متقسم على جزأين: n8n هو اللي بيكلّم واتساب، و.NET هو العقل اللي بيقرر. والداتابيز نفسها بتمنع أي جزء يكتب في جداول التاني، فمفيش لخبطة.");

s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الحالات والنظام" });
ph(s, "حاجات حازم مش بيعملها أبدًا", "title");
[
  ["T-07", "ما بيقولش رأي قانوني", "مفيش «قضيتك مضمونة» ولا «العقد ده سليم». السؤال القانوني مكانه الاستشارة."],
  ["T-09", "ما بيبعتش ورق لأي AI", "البطاقات والإيصالات والعقود بتتحفظ في مخزن حساس، وما بتطلعش برا."],
  ["T-19", "ما بيبعتش نفس الرسالة مرتين", "كل رسالة ليها مفتاح مميز، فحتى لو حاجة اتعادت ما تتكررش عند العميل."],
  ["M-02", "ما بيخالفش قواعد واتساب", "بعد 24 ساعة من آخر رسالة للعميل، مفيش غير قوالب معتمدة من Meta."],
  ["الأسعار", "ما بيخترعش سعر ولا خصم", "الأسعار من جدول المكتب بس، وأي استثناء بيروح للمسؤول."],
  ["B-12", "ما بيتكلمش فوق الموظف", "أول ما الموظف يرد، حازم يسكت."],
].forEach(([code, h, p], i) => {
  const cols = 2, gap = 0.6;
  const cw = (W - 2 * M - gap) / cols, rh = 1.62;
  const col = i % cols, row = Math.floor(i / cols);
  const x = W - M - cw - col * (cw + gap), y = 1.55 + row * rh;
  chip(s, code, x + cw - 1.1, y + 0.08, { w: 1.1, fill: C.accent3, line: C.accent3, color: C.accent1 });
  txt(s, h, { x, y: y + 0.02, w: cw - 1.35, h: 0.5, fontSize: 20, bold: true });
  txt(s, p, { x, y: y + 0.55, w: cw - 1.35, h: 0.9, fontSize: 15, color: C.accent6, valign: "top" });
});
s.addNotes("دي الحدود اللي اتفقنا عليها مع المكتب. كل قاعدة ليها رقم في ملف القرارات، ومتطبقة في الكود وعليها اختبارات أوتوماتيك.");

pres.addSection({ title: "الخطوة الجاية" });
s = pres.addSlide({ masterName: "CONTENT", sectionTitle: "الخطوة الجاية" });
ph(s, "محتاجين إيه من المكتب", "title");
[
  ["عند Meta", "توثيق النشاط", "Business Verification، وبياخد أيام. وبعده اسم العرض «مكتب الأستاذ سعد فتحي سعد»."],
  ["عند Meta", "تطبيق Meta ومفاتيحه", "App وحساب واتساب للأعمال وtoken دائم، ويتدخلوا مباشرة في النظام."],
  ["المكتب", "بيانات التشغيل", "العنوان، ورقم التواصل، وبيانات InstaPay والمحفظة، والموظفين، والموكلين الحاليين."],
  ["قرار المكتب", "نقل رقم المكتب", "بعد التجربة على رقم اختبار، ومع backup لمحادثات الواتساب الحالية."],
  ["قبل الإطلاق", "دومين وسيرفر", "رابط ثابت وسيرفر للإنتاج، بدل الرابط المؤقت بتاع الاختبار."],
].forEach(([who, h, p], i) => {
  const y = 1.5 + i * 1.02;
  s.addShape(pres.shapes.OVAL, { x: W - M - 0.55, y: y + 0.12, w: 0.55, h: 0.55, fill: { color: C.accent1 }, line: { type: "none" } });
  txt(s, String(i + 1), { x: W - M - 0.55, y: y + 0.12, w: 0.55, h: 0.55, fontSize: 18, bold: true, color: C.background1, align: "center", valign: "middle", rtlMode: false });
  txt(s, who, { x: M, y: y - 0.02, w: W - 2 * M - 0.85, h: 0.26, fontSize: 11, bold: true, color: C.accent2 });
  txt(s, h, { x: M, y: y + 0.22, w: W - 2 * M - 0.85, h: 0.38, fontSize: 19, bold: true });
  txt(s, p, { x: M, y: y + 0.6, w: W - 2 * M - 0.85, h: 0.34, fontSize: 14, color: C.accent6 });
});
s.addNotes("عشان حازم يشتغل مع عملاء حقيقيين، محتاجين خمس حاجات من المكتب. أهمهم توثيق Meta لأنه بياخد أيام، فالأحسن يبدأ النهارده.");

s = pres.addSlide({ masterName: "COVER", sectionTitle: "الخطوة الجاية" });
ph(s, "الخطوة الجاية", "title");
ph(s, [
  { text: "فهم حازم: تصنيف نية العميل والردود المعتمدة", options: { breakLine: true } },
  { text: "وبالتوازي: المكتب يبدأ توثيق Meta", options: { fontSize: 18 } },
], "body");
s.addNotes("المرحلة الجاية حازم يبتدي يفهم العميل ويرد بالنصوص المعتمدة. وفي نفس الوقت محتاجين المكتب يبدأ توثيق Meta، عشان أول ما نخلص نجرب على واتساب الحقيقي.");

(async () => {
  await pres.writeFile({ fileName: OUT });
  await applyTheme(OUT, THEME);
  console.log("written", OUT);
})();
