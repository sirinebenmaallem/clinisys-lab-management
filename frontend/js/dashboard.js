/* global App, Ui, Api, jQuery */
"use strict";
const Dashboard = (() => {
  function amount(text) {
    if (!/^\d+\.\d{2}$/.test(text)) return "Indisponible";
    const [whole, cents] = text.split(".");
    return whole.replace(/\B(?=(\d{3})+(?!\d))/g, "\u202f") + "," + cents;
  }
  function dailySeries(from, to, days) {
    const counts = new Map(days.map(day => [day.date, day.requestCount]));
    const start = Date.parse(from + "T00:00:00Z"), end = Date.parse(to + "T00:00:00Z");
    if (!Number.isFinite(start) || !Number.isFinite(end) || end < start || end - start > 365 * 86400000) return [];
    const values = [];
    for (let time = start; time <= end; time += 86400000) {
      const date = new Date(time).toISOString().slice(0, 10);
      values.push({ date, requestCount: counts.get(date) || 0 });
    }
    return values;
  }
  async function page(state = {}) {
    const now = new Date(), today = now.toISOString().slice(0, 10);
    const from = state.from || today.slice(0, 8) + "01", to = state.to || today;
    Ui.heading("Tableau de bord", "Activité et valeur nette des demandes — période en UTC");
    const root = jQuery("#page");
    const start = Ui.input("from", "Début inclus (UTC)", "date").val(from).prop("required", true);
    const end = Ui.input("to", "Fin incluse (UTC)", "date").val(to).prop("required", true);
    root.append(Ui.searchForm([Ui.el("label", "Début inclus (UTC)").append(start), Ui.el("label", "Fin incluse (UTC)").append(end)], values => App.open("dashboard", values)),
      Ui.el("p", "Chiffre d’affaires : valeur nette des demandes validées ou délivrées, créées pendant la période. Il ne représente pas un encaissement : aucun suivi des paiements n’existe. Remises et arrondis appliqués par ligne. Maximum 366 jours.", { class: "note" }));
    const output = Ui.el("section", "Chargement des statistiques…", { "aria-live": "polite", "aria-busy": "true" });
    root.append(output);
    try {
      const data = await Api.get("/api/dashboard", { from, to });
      output.empty().append(Ui.el("div", "", { class: "panel grid" }).append(Ui.stat("Demandes créées", Ui.number(data.requestCount)), Ui.stat("Chiffre d’affaires net validé (hors encaissements)", amount(data.revenueText))));
      if (!data.requestCount) { output.append(Ui.el("p", "Aucune demande créée pendant cette période.", { class: "empty panel" })); return; }
      const days = dailySeries(data.from, data.to, data.days), max = Math.max(1, ...days.map(d => d.requestCount));
      const bars = Ui.el("div", "", { class: "dashboard-bars", role: "img", "aria-label": "Demandes créées par jour. Dates et nombres détaillés dans le tableau suivant." });
      days.forEach(day => bars.append(Ui.el("div", "", { class: "dashboard-bar", title: day.date + " : " + day.requestCount }).css("height", day.requestCount / max * 160 + "px")));
      output.append(Ui.el("h2", "Demandes par jour"), bars, Ui.el("p", from + " → " + to + " (UTC)", { class: "small muted" }),
        Ui.table(["Jour (UTC)", "Demandes créées"], days.map(day => [day.date, Ui.number(day.requestCount)])),
        Ui.el("h2", "Analyses les plus demandées — top 10"), Ui.el("p", "Tous les statuts ; une analyse compte une fois par demande. Classement décroissant, puis code en cas d’égalité.", { class: "small muted" }),
        Ui.table(["Rang", "Code", "Analyse", "Nombre de demandes"], data.topAnalyses.map((a, i) => [i + 1, a.code, a.name, Ui.number(a.requestCount)])));
    } catch (error) {
      if (!error.cancelled && App.session) output.empty().append(Ui.el("p", error.message, { role: "alert", class: "error" }), Ui.button("Réessayer", () => App.open("dashboard", { from, to })));
    } finally { output.attr("aria-busy", "false"); }
  }
  return { amount, dailySeries, page };
})();
if (typeof module !== "undefined") module.exports = Dashboard;
else App.pages.dashboard = Dashboard.page;
