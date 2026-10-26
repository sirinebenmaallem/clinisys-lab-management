/* global App, Ui, Api, jQuery */
"use strict";
const PatientHistory = (() => {
  const labels = { Low: "Basse", Normal: "Normale", High: "Haute", NotEvaluated: "Non évaluée" };
  // Exact snapshot units only: never infer a conversion or join incompatible units.
  function series(points) {
    const groups = new Map();
    for (const point of [...points].sort((a, b) => Date.parse(a.requestedAt) - Date.parse(b.requestedAt))) {
      if (!groups.has(point.unit)) groups.set(point.unit, []);
      groups.get(point.unit).push(point);
    }
    return [...groups].map(([unit, values]) => ({ unit, points: values }));
  }
  function chart(group) {
    const svgNode = (name, attrs, text) => {
      const node = document.createElementNS("http://www.w3.org/2000/svg", name);
      Object.entries(attrs || {}).forEach(([key, value]) => node.setAttribute(key, value));
      if (text != null) node.textContent = text;
      return node;
    };
    const points = group.points, values = points.map(p => Number(p.value)), times = points.map(p => Date.parse(p.requestedAt));
    const min = Math.min(...values), max = Math.max(...values), start = times[0], end = times[times.length - 1];
    const x = i => end === start ? 430 : 100 + (times[i] - start) / (end - start) * 660;
    const y = i => max === min ? 135 : 225 - (values[i] - min) / (max - min) * 180;
    const svg = svgNode("svg", { viewBox: "0 0 860 290", role: "img", "aria-label": "Évolution chronologique en " + group.unit + ". Valeurs détaillées dans le tableau suivant.", class: "history-chart" });
    svg.append(svgNode("title", {}, "Évolution en " + group.unit), svgNode("path", { d: "M100 30 V225 H760", fill: "none", stroke: "#647a80" }));
    for (const [value, position] of [[max, 48], [min, 225]]) svg.append(svgNode("text", { x: 92, y: position, "text-anchor": "end", "font-size": 12 }, Ui.number(value)));
    svg.append(svgNode("text", { x: 100, y: 260, "font-size": 12 }, Ui.date(points[0].requestedAt)),
      svgNode("text", { x: 760, y: 280, "text-anchor": "end", "font-size": 12 }, Ui.date(points[points.length - 1].requestedAt)));
    if (points.length > 1) svg.append(svgNode("polyline", { points: points.map((_, i) => x(i) + "," + y(i)).join(" "), fill: "none", stroke: "#087f79", "stroke-width": 2 }));
    points.forEach((p, i) => {
      const circle = svgNode("circle", { cx: x(i), cy: y(i), r: 5, fill: p.abnormality === "High" ? "#9a231b" : p.abnormality === "Low" ? "#174a85" : p.abnormality === "Normal" ? "#17623c" : "#647a80" });
      circle.append(svgNode("title", {}, `${Ui.date(p.requestedAt)} : ${Ui.number(p.value)} ${group.unit} — ${labels[p.abnormality] || "Non évaluée"}`));
      svg.append(circle);
    });
    return jQuery(svg);
  }
  async function page(state) {
    const patient = state.patient, selected = state.analysis, pageNumber = state.page || 1;
    Ui.heading("Historique du patient", patient.firstName + " " + patient.lastName, [Ui.button("Patients", () => App.open("patients"))]);
    const root = jQuery("#page");
    if (selected) {
      root.append(Ui.el("h2", selected.name));
      const output = Ui.el("section", "Chargement de l’historique…", { "aria-live": "polite", "aria-busy": "true" });
      root.append(output);
      try {
        const data = await Api.get(`/api/patients/${encodeURIComponent(patient.id)}/analyses/${encodeURIComponent(selected.id)}/history`);
        output.empty();
        if (!data.points.length) output.append(Ui.el("p", "Aucun résultat validé ou délivré pour cette analyse.", { class: "empty panel" }));
        else {
          output.append(Ui.el("p", "Dates des demandes en UTC. Résultats validés ou délivrés uniquement ; les unités différentes sont représentées séparément.", { class: "note" }));
          for (const group of series(data.points)) output.append(Ui.el("figure", "", { class: "panel" }).append(Ui.el("figcaption", "Unité : " + group.unit), chart(group)));
          output.append(Ui.table(["Demande", "Date de demande (UTC)", "Date du résultat (UTC)", "Valeur", "Unité", "Évaluation"], data.points.map(p => [p.requestNumber, Ui.date(p.requestedAt), Ui.date(p.resultAt), Ui.number(p.value), p.unit, labels[p.abnormality] || "Non évaluée"])));
        }
      } catch (error) {
        if (error.cancelled || !App.session) return;
        if (!error.cancelled) output.empty().append(Ui.el("p", error.message, { role: "alert", class: "error" }), Ui.button("Réessayer", () => App.open("history", state)));
      } finally { output.attr("aria-busy", "false"); }
    } else root.append(Ui.el("p", "Choisissez une analyse pour consulter son évolution."));
    const catalogue = await Api.get("/api/medical-analyses", { page: pageNumber, pageSize: 20, search: state.search || "" });
    const search = Ui.input("search", "Code ou nom de l’analyse").val(state.search || "");
    root.append(Ui.el("h2", "Choisir une analyse"), Ui.searchForm([search], values => App.open("history", { ...state, search: values.search, page: 1 })),
      Ui.table(["Code", "Analyse", "Action"], catalogue.items.map(a => [a.code, a.name, Ui.button("Afficher l’évolution", () => App.open("history", { ...state, analysis: a }))])),
      Ui.pager(pageNumber, catalogue.totalCount, 20, p => App.open("history", { ...state, page: p })));
  }
  return { series, page };
})();
if (typeof module !== "undefined") module.exports = PatientHistory;
else App.pages.history = PatientHistory.page;
