/* global jQuery, App, Ui, Api */
"use strict";
window.Patients = {
  async edit(id) {
    const p = id ? await Api.get("/api/patients/" + encodeURIComponent(id)) : {};
    Ui.modal(id ? "Modifier le patient" : "Nouveau patient", [
      { name: "firstName", label: "Prénom", value: p.firstName, maxLength: 100 }, { name: "lastName", label: "Nom", value: p.lastName, maxLength: 100 },
      { name: "dateOfBirth", label: "Date de naissance", type: "date", value: p.dateOfBirth },
      { name: "sex", label: "Sexe", value: p.sex || "Unspecified", options: Object.entries(Ui.sex) },
      { name: "nationalIdentityNumber", label: "CIN", value: p.nationalIdentityNumber, required: false, maxLength: 50 },
      { name: "phoneNumber", label: "Téléphone", value: p.phoneNumber, required: false, maxLength: 30, help: "Au moins un identifiant : CIN ou téléphone." }
    ], async values => {
      values.nationalIdentityNumber = values.nationalIdentityNumber.trim() || null; values.phoneNumber = values.phoneNumber.trim() || null;
      await Api.write(id ? "PUT" : "POST", "/api/patients" + (id ? "/" + encodeURIComponent(id) : ""), id ? { patient: values, version: p.version } : values);
      await App.open("patients");
    });
  },
  async detail(id) {
    const p = await Api.get("/api/patients/" + encodeURIComponent(id));
    Ui.modal("Patient — consultation", [
      { name: "firstName", label: "Prénom", value: p.firstName }, { name: "lastName", label: "Nom", value: p.lastName },
      { name: "dateOfBirth", label: "Naissance", type: "date", value: p.dateOfBirth }, { name: "sex", label: "Sexe", value: p.sex, options: Object.entries(Ui.sex) },
      { name: "cin", label: "CIN", value: p.nationalIdentityNumber, required: false }, { name: "phone", label: "Téléphone", value: p.phoneNumber, required: false }
    ], async () => {});
    jQuery("#editor-body input,#editor-body select").prop("disabled", true);
    jQuery("#editor-body button[type=submit]").remove();
  },
  // Also used by request registration. Suggestions never expose CIN/phone fields.
  picker(container, selected, initialPatient) {
    let timer, sequence = 0;
    const search = Ui.input("patientSearch", "Nom, CIN exact ou téléphone exact"), list = Ui.el("ul", "", { class: "suggestions" }), choice = Ui.el("div");
    container.append(search, list, choice);
    if (initialPatient) choice.append(Ui.el("div", "Patient sélectionné : " + initialPatient.firstName + " " + initialPatient.lastName, { class: "selected-patient" }));
    search.on("input", () => {
      clearTimeout(timer); const ticket = ++sequence; selected(null); choice.empty(); list.empty();
      timer = setTimeout(async () => {
        if (!search[0].isConnected || !search.val().trim()) return;
        try {
          const page = await Api.get("/api/patients", { search: search.val().trim(), page: 1, pageSize: 20 });
          if (ticket !== sequence || !search[0].isConnected) return;
          list.empty();
          if (!page.items.length) list.append(Ui.el("li", "Aucun patient trouvé.", { class: "muted small" }));
          page.items.forEach(p => list.append(Ui.el("li").append(Ui.button(p.firstName + " " + p.lastName + " · " + Ui.date(p.dateOfBirth), () => {
            selected(p); list.empty(); choice.empty().append(Ui.el("div", "Patient sélectionné : " + p.firstName + " " + p.lastName, { class: "selected-patient" }));
          }))));
        } catch (error) { if (!error.cancelled && ticket === sequence) list.empty().append(Ui.el("li", error.message, { class: "error small" })); }
      }, 300);
    });
  }
};
App.pages.patients = async function (state = {}) {
  const page = state.page || 1, search = state.search || "";
  const data = await Api.get("/api/patients", { page, pageSize: 20, search });
  Ui.heading("Patients", "Recherche par nom, CIN normalisé ou téléphone exact.", App.has("Secretary") ? [Ui.button("+ Nouveau patient", () => Patients.edit(), "primary")] : []);
  const field = Ui.input("search", "Rechercher un patient").val(search).addClass("search");
  jQuery("#page").append(Ui.searchForm([field], v => App.open("patients", { search: v.search.trim(), page: 1 })));
  if (!App.has("Secretary")) jQuery("#page").append(Ui.el("p", "La consultation des identifiants et la modification sont réservées au secrétariat.", { class: "note" }));
  jQuery("#page").append(Ui.table(["Patient", "Naissance", "Sexe", "Actions"], data.items.map(p => [p.firstName + " " + p.lastName, Ui.date(p.dateOfBirth), Ui.sex[p.sex], Ui.el("div", "", { class: "actions" }).append(
    App.has("Technician", "Biologist") ? [Ui.button("Historique", () => App.open("history", { patient: p }))] : [], App.has("Secretary") ? [Ui.button("Consulter", () => Patients.detail(p.id)), Ui.button("Modifier", () => Patients.edit(p.id)), Ui.button("Créer une demande", () => App.open("newRequest", p))] : [])])), Ui.pager(page, data.totalCount, 20, next => App.open("patients", { page: next, search })));
};
