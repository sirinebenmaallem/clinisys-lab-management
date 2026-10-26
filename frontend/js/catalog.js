/* global App, Ui, Api, jQuery */
"use strict";
(() => {
  const base = kind => kind === "panels" ? "/api/analysis-panels" : "/api/medical-analyses";
  async function edit(kind, item) {
    const fields = [{ name: "code", label: "Code", value: item?.code }, { name: "name", label: "Nom", value: item?.name }];
    if (kind !== "panels") fields.push({ name: "unit", label: "Unité", value: item?.unit }, { name: "price", label: "Prix", type: "number", min: 0, value: item?.price });
    Ui.modal(item ? "Modifier" : "Créer", fields, async values => {
      await Api.write(item ? "PUT" : "POST", base(kind) + (item ? "/" + item.id : ""), values);
      await App.open("catalog", { kind });
    });
  }
  App.pages.catalog = async (state = {}) => {
    const kind = state.kind || "analyses", page = state.page || 1;
    const query = { page, pageSize: 20, search: state.search || "" };
    if (state.isActive) query.isActive = state.isActive;
    const data = await Api.get(base(kind), query);
    Ui.heading("Catalogue", "Analyses, bilans et références médicales", App.has("Biologist") ? [Ui.button("Créer", () => edit(kind), "primary")] : []);
    const root = jQuery("#page");
    root.append(Ui.el("div", "", { class: "tabs" }).append(Ui.button("Analyses", () => App.open("catalog")), Ui.button("Bilans", () => App.open("catalog", { kind: "panels" }))));
    root.append(Ui.searchForm([Ui.input("search", "Code ou nom").val(state.search || ""), Ui.select("isActive", "Activation", [["", "Tous"], ["true", "Actifs"], ["false", "Inactifs"]]).val(state.isActive || "")], values => App.open("catalog", { ...values, kind })));
    root.append(Ui.table(["Code", "Nom", ...(kind === "panels" ? [] : ["Unité", "Prix"]), "État", "Actions"], data.items.map(item => {
      const actions = Ui.el("div", "", { class: "actions" }).append(Ui.button(kind === "panels" ? "Composition" : "Références", () => App.open(kind === "panels" ? "panel" : "ranges", item)));
      if (App.has("Biologist")) actions.append(Ui.button("Modifier", () => edit(kind, item)), item.isActive ? Ui.button("Désactiver", async () => { if (confirm("Désactiver cet élément du catalogue ?")) { await Api.write("POST", base(kind) + "/" + item.id + "/deactivate"); await App.open("catalog", state); } }) : []);
      return [item.code, item.name, ...(kind === "panels" ? [] : [item.unit, Ui.number(item.price)]), item.isActive ? "Actif" : "Inactif", actions];
    })), Ui.pager(page, data.totalCount, 20, p => App.open("catalog", { ...state, kind, page: p })));
  };
  App.pages.ranges = async analysis => {
    const path = "/api/medical-analyses/" + analysis.id + "/reference-ranges", ranges = await Api.get(path);
    const editRange = range => Ui.modal("Plage de référence", [
      { name: "sex", label: "Sexe exact", options: Object.entries(Ui.sex), value: range?.sex || "Unspecified" },
      { name: "minimumAgeDays", label: "Âge minimum inclus (jours)", type: "number", min: 0, step: 1, value: range?.minimumAgeDays ?? 0 },
      { name: "maximumAgeDays", label: "Âge maximum exclu (jours)", type: "number", min: 0, step: 1, required: false, value: range?.maximumAgeDays },
      { name: "lowerBound", label: "Borne basse inclusive", type: "number", value: range?.lowerBound },
      { name: "upperBound", label: "Borne haute inclusive", type: "number", value: range?.upperBound }
    ], async values => { await Api.write(range ? "PUT" : "POST", path + (range ? "/" + range.id : ""), values); await App.open("ranges", analysis); });
    Ui.heading(analysis.name, "Références en " + analysis.unit + " · âge en jours", [Ui.button("Catalogue", () => App.open("catalog")), ...(App.has("Biologist") ? [Ui.button("Ajouter", () => editRange(), "primary")] : [])]);
    jQuery("#page").append(Ui.table(["Sexe", "Âge minimum", "Maximum exclu", "Borne basse", "Borne haute", "Actions"], ranges.map(r => [Ui.sex[r.sex], r.minimumAgeDays, r.maximumAgeDays ?? "Sans limite", Ui.number(r.lowerBound), Ui.number(r.upperBound), App.has("Biologist") ? Ui.el("div", "", { class: "actions" }).append(Ui.button("Modifier", () => editRange(r)), Ui.button("Supprimer", async () => { if (confirm("Supprimer cette plage ? Les résultats existants conservent leurs références figées.")) { await Api.write("DELETE", path + "/" + r.id); await App.open("ranges", analysis); } })) : "—"])));
  };
  App.pages.panel = async (panel, page = 1, search = "") => {
    const path = "/api/analysis-panels/" + panel.id, detail = await Api.get(path);
    Ui.heading(detail.name, "Composition du bilan", [Ui.button("Bilans", () => App.open("catalog", { kind: "panels" }))]);
    const root = jQuery("#page");
    root.append(Ui.table(["Code", "Analyse", "Unité", "Prix", "Actions"], detail.analyses.map(a => [a.code, a.name, a.unit, Ui.number(a.price), App.has("Biologist") ? Ui.button("Retirer", async () => { await Api.write("DELETE", path + "/analyses/" + a.id); await App.open("panel", panel); }) : "—"])));
    if (!App.has("Biologist")) return;
    root.append(Ui.el("h2", "Ajouter une analyse"));
    async function choices(p, term) {
      const data = await Api.get("/api/medical-analyses", { page: p, pageSize: 20, search: term, isActive: true });
      box.empty().append(Ui.table(["Code", "Analyse", "Action"], data.items.map(a => [a.code, a.name, Ui.button("Ajouter", async () => { await Api.write("PUT", path + "/analyses/" + a.id); await App.open("panel", panel); }).prop("disabled", detail.analyses.some(x => x.id === a.id))])), Ui.pager(p, data.totalCount, 20, next => choices(next, term)));
    }
    const box = Ui.el("div");
    root.append(Ui.searchForm([Ui.input("search", "Rechercher une analyse")], values => choices(1, values.search)), box);
    await choices(page, search);
  };
})();
