/* global App, Ui, Api, Patients, ResultEntry, jQuery */
"use strict";
(() => {
  const base = "/api/laboratory-requests";
  App.pages.requests = async (state = {}) => {
    const page = state.page || 1, query = { page, pageSize: 20 };
    for (const key of ["number", "status", "patientId"]) if (state[key]) query[key] = state[key];
    for (const key of ["registeredFrom", "registeredTo"]) if (state[key]) query[key] = state[key] + "T00:00:00Z";
    const data = await Api.get(base, query);
    Ui.heading("Demandes d’analyses", "Suivi des demandes et comptes rendus", App.has("Secretary") ? [Ui.button("Nouvelle demande", () => App.open("newRequest"), "primary")] : []);
    jQuery("#page").append(Ui.searchForm([
      Ui.input("number", "Numéro de demande").val(state.number || ""),
      Ui.select("status", "Statut", [["", "Tous les statuts"], ...Object.entries(Ui.statuses)]).val(state.status || ""),
      Ui.input("registeredFrom", "Depuis (UTC)", "date").val(state.registeredFrom || ""), Ui.input("registeredTo", "Avant (UTC, exclu)", "date").val(state.registeredTo || "")
    ], values => App.open("requests", { ...values, patientId: state.patientId })), Ui.el("p", "Les filtres de dates utilisent minuit UTC ; la date de fin est exclue.", { class: "small muted" }),
    Ui.table(["Numéro", "Patient", "Enregistrement UTC", "Statut", "Total", "Action"], data.items.map(r => [r.number, [r.patient?.currentFirstName, r.patient?.currentLastName].filter(Boolean).join(" ") || r.patientId, Ui.date(r.registeredAt), Ui.badge(r.status), Ui.number(r.total), Ui.button("Ouvrir", () => App.open("request", r.id))])), Ui.pager(page, data.totalCount, 20, p => App.open("requests", { ...state, page: p })));
  };
  App.pages.newRequest = async initialPatient => {
    Ui.heading("Nouvelle demande", "Le serveur attribue le numéro et calcule les tarifs.", [Ui.button("Retour", () => App.open("requests"))]);
    const root = jQuery("#page"), form = Ui.el("form", "", { class: "panel" }), patientBox = Ui.el("div"), selected = { analyses: new Map(), panels: new Map() };
    let patient = initialPatient || null;
    form.append(Ui.el("h2", "Patient"));
    form.append(patientBox); Patients.picker(patientBox, value => { patient = value; }, initialPatient);
    const physician = Ui.input("physician", "Médecin prescripteur"), discount = Ui.input("discount", "Remise (%)", "number").attr({ min: 0, max: 100, step: "any" }).val(0), summary = Ui.el("p", "Aucune analyse sélectionnée.");
    physician.prop("required", true).attr("maxlength", 200);
    discount.prop("required", true);
    form.append(Ui.el("label", "Médecin prescripteur"), physician, Ui.el("label", "Remise (%)"), discount, Ui.el("h2", "Analyses et bilans"), summary);
    const choices = Ui.el("div"), kindInput = Ui.select("kind", "Type", [["analyses", "Analyses"], ["panels", "Bilans"]]), search = Ui.input("search", "Code ou nom"), toolbar = Ui.el("div", "", { class: "toolbar" });
    async function load(kind, page, term) {
      const data = await Api.get(kind === "analyses" ? "/api/medical-analyses" : "/api/analysis-panels", { page, pageSize: 20, search: term, isActive: true });
      choices.empty().append(Ui.table(["Choix", "Code", "Nom", "Prix"], data.items.map(item => {
        const check = Ui.el("input", "", { type: "checkbox", "aria-label": "Sélectionner " + item.name }).prop("checked", selected[kind].has(item.id)).on("change", function () {
          if (this.checked) selected[kind].set(item.id, item); else selected[kind].delete(item.id);
          summary.text([...selected.analyses.values(), ...selected.panels.values()].map(x => x.name).join(" · ") || "Aucune analyse sélectionnée.");
        });
        return [check, item.code, item.name, kind === "analyses" ? Ui.number(item.price) : "Calculé par le serveur"];
      })), Ui.pager(page, data.totalCount, 20, p => load(kind, p, term)));
    }
    toolbar.append(kindInput, search, Ui.button("Rechercher", () => load(kindInput.val(), 1, search.val())));
    form.append(toolbar, choices, Ui.el("p", "Les analyses communes aux bilans et aux choix individuels sont dédoublonnées par le serveur.", { class: "small muted" }), Ui.el("button", "Créer la demande", { type: "submit", class: "primary" }));
    form.on("submit", event => { event.preventDefault(); App.perform(async () => {
      if (!patient) throw new Error("Sélectionnez un patient dans la recherche.");
      const result = await Api.write("POST", base, { patientId: patient.id, prescribingPhysician: physician.val().trim(), discountPercent: Number(discount.val()), analysisIds: [...selected.analyses.keys()], panelIds: [...selected.panels.keys()] });
      await App.open("request", result.id);
    }); });
    root.append(form); await load("analyses", 1, "");
  };
  App.pages.request = async id => {
    const detail = await Api.get(base + "/" + id), editable = ["Registered", "InProgress", "ResultsEntered"].includes(detail.status), actions = [Ui.button("Demandes", () => App.open("requests"))];
    const transitions = { Registered: ["start", "Démarrer", ["Technician", "Biologist"]], InProgress: ["complete-entry", "Terminer la saisie", ["Technician", "Biologist"]], ResultsEntered: ["validate", "Valider biologiquement", ["Biologist"]], Validated: ["deliver", "Délivrer", ["Secretary", "Biologist"]] };
    const transition = transitions[detail.status];
    if (transition && App.has(...transition[2])) actions.push(Ui.button(transition[1], async () => {
      if (!confirm(transition[1] + " cette demande ?")) return;
      await Api.write("POST", base + "/" + id + "/" + transition[0], { version: detail.version }); await App.open("request", id);
    }, "primary"));
    if (["Validated", "Delivered"].includes(detail.status)) actions.push(Ui.button("Télécharger le PDF", () => Api.download(id), "primary"));
    Ui.heading(detail.number, "Détail de la demande · dates en UTC", actions);
    const root = jQuery("#page");
    root.append(Ui.el("div", "", { class: "panel grid" }).append(Ui.stat("Patient", [detail.patient.currentFirstName, detail.patient.currentLastName].join(" ")), Ui.stat("Naissance (snapshot)", Ui.date(detail.patientDateOfBirthSnapshot)), Ui.stat("Sexe (snapshot)", Ui.sex[detail.patientSexSnapshot]), Ui.stat("Statut", Ui.statuses[detail.status]), Ui.stat("Enregistrée", Ui.date(detail.registeredAt)), Ui.stat("Prescripteur", detail.prescribingPhysician || "—"), Ui.stat("Remise", Ui.number(detail.discountPercent) + " %"), Ui.stat("Total", Ui.number(detail.total))));
    if (detail.panels.length) root.append(Ui.el("p", "Bilans : " + detail.panels.map(p => p.panelNameSnapshot).join(" · ")));
    root.append(Ui.table(["Code", "Analyse", "Unité", "Prix initial", "Remise (%)", "Net"], detail.items.map(i => [i.analysisCodeSnapshot, i.analysisNameSnapshot, i.unitSnapshot, Ui.number(i.unitPriceSnapshot), Ui.number(i.discountPercentSnapshot), Ui.number(i.netPrice)])));
    if (!App.has("Technician", "Biologist")) { root.append(Ui.el("p", "La consultation des résultats est réservée aux techniciens et biologistes.", { class: "note" })); return; }
    const results = await Api.get(base + "/" + id + "/results");
    root.append(Ui.el("h2", "Résultats numériques"));
    const abnormal = { NotEvaluated: "Non évalué", Normal: "Normale", Low: "Basse", High: "Haute" };
    root.append(Ui.table(["Analyse", "Résultat", "Unité", "Références figées", "Évaluation", "Dernière saisie UTC", "Action"], results.items.map(item => {
      const r = item.result;
      const value = r ? Ui.el("span", Ui.number(r.value) + (r.isCalculated ? " · calculé" : ""), { class: ["Low", "High"].includes(r.abnormality) ? "abnormal" : "" }) : "Non saisi";
      const action = editable && !r?.validatedAt && !r?.validatedBy && !r?.isCalculated ? Ui.button(r ? "Corriger" : "Saisir", () => { const form = Ui.modal(item.analysisNameSnapshot, [{ name: "value", label: "Valeur en " + item.unitSnapshot, type: "number", value: r?.value }], async values => {
        await Api.write(r ? "PUT" : "POST", base + "/" + id + "/items/" + item.requestItemId + "/result", r ? { value: values.value, version: r.version } : values); await App.open("request", id);
      }); ResultEntry.attach(form, item.reference); }) : "Verrouillé";
      return [item.analysisNameSnapshot, value, item.unitSnapshot, r && (r.referenceLowerBoundSnapshot != null || r.referenceUpperBoundSnapshot != null) ? Ui.number(r.referenceLowerBoundSnapshot) + " à " + Ui.number(r.referenceUpperBoundSnapshot) : "Non disponibles", r ? Ui.el("span", abnormal[r.abnormality], { class: "result-evaluation " + r.abnormality }) : "—", r ? Ui.date(r.enteredAt) : "—", action];
    })));
    if (results.ldlCalculation) {
      const labels = { NotConfigured: "codes du catalogue non configurés", AmbiguousAnalysis: "analyses ambiguës", LdlNotRequested: "LDL non demandé", ManualResultPreserved: "résultat manuel conservé", MissingSource: "résultats sources manquants", IncompatibleUnits: "unités incompatibles ou non reconnues", InvalidSource: "valeur source invalide", TriglyceridesTooHigh: "triglycérides au seuil ou au-dessus", InvalidCalculatedValue: "valeur calculée invalide", Calculated: "calcul effectué" };
      root.append(Ui.el("p", "Calcul LDL : " + (labels[results.ldlCalculation.status] || results.ldlCalculation.status) + (results.ldlCalculation.value != null ? " · " + Ui.number(results.ldlCalculation.value) : ""), { class: "note" }));
    }
    root.append(Ui.el("p", "La saisie ne valide pas biologiquement la demande. Les références sont figées lors de la première saisie réussie.", { class: "small muted" }));
  };
})();
