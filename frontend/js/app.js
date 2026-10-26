/* global jQuery, Api */
"use strict";
window.Ui = (() => {
  const el = (tag, text, attrs) => jQuery("<" + tag + ">", attrs || {}).text(text ?? "");
  const button = (text, action, style = "") => el("button", text, { type: "button", class: style }).on("click", () => App.perform(action));
  const number = value => value == null ? "—" : Number(value).toLocaleString("fr-FR", { maximumFractionDigits: 6 });
  const date = value => value ? new Date(value.length === 10 ? value + "T00:00:00Z" : value).toLocaleString("fr-FR", { timeZone: "UTC", ...(value.length === 10 ? { year: "numeric", month: "2-digit", day: "2-digit" } : {}) }) : "—";
  const statuses = { Registered: "Enregistrée", InProgress: "En cours", ResultsEntered: "Saisie", Validated: "Validée", Delivered: "Délivrée" };
  const sex = { Unspecified: "Non précisé", Female: "Féminin", Male: "Masculin" };
  const badge = status => el("span", statuses[status] || status, { class: "badge " + (["Validated", "Delivered"].includes(status) ? "good" : "") });
  function heading(title, subtitle, actions = []) {
    jQuery("#page").empty().append(el("div", "", { class: "page-head" }).append(el("div").append(el("h1", title), el("p", subtitle, { class: "muted" })), el("div", "", { class: "actions" }).append(actions)));
  }
  function table(headers, rows) {
    if (!rows.length) return el("div", "Aucun résultat pour ces critères.", { class: "empty panel" });
    const t = el("table"), head = el("tr"); headers.forEach(h => head.append(el("th", h, { scope: "col" })));
    const body = el("tbody"); rows.forEach(cells => { const row = el("tr"); cells.forEach(cell => row.append(el("td").append(cell?.jquery ? cell : document.createTextNode(String(cell ?? "—"))))); body.append(row); });
    return el("div", "", { class: "table-wrap" }).append(t.append(el("thead").append(head), body));
  }
  function pager(page, total, pageSize, load) {
    return el("div", "", { class: "pager" }).append(button("← Précédente", () => load(page - 1)).prop("disabled", page <= 1), el("span", "Page " + page + " · " + total + " résultat(s)", { class: "small muted" }), button("Suivante →", () => load(page + 1)).prop("disabled", page * pageSize >= total));
  }
  function input(name, placeholder, type = "text") { return el("input", "", { name, placeholder, type, "aria-label": placeholder }); }
  function select(name, label, choices) {
    const s = el("select", "", { name, "aria-label": label }); choices.forEach(c => s.append(el("option", c[1], { value: c[0] }))); return s;
  }
  function searchForm(inputs, submit) {
    const form = el("form", "", { class: "toolbar" }).append(inputs, el("button", "Rechercher", { type: "submit" }));
    form.on("submit", event => { event.preventDefault(); App.perform(() => submit(Object.fromEntries(new FormData(form[0])))); }); return form;
  }
  function modal(title, fields, save) {
    jQuery("#editor-title").text(title); jQuery("#editor-error").empty();
    const form = el("form"), grid = el("div", "", { class: "form-grid" });
    fields.forEach(f => {
      const id = "field-" + f.name, wrapper = el("div", "", { class: f.wide ? "wide" : "" });
      const input = f.options ? select(f.name, f.label, f.options) : el("input", "", { name: f.name, type: f.type || "text" });
      input.attr("id", id).prop("required", f.required !== false).val(f.value ?? "");
      if (f.maxLength) input.attr("maxlength", f.maxLength);
      if (f.type === "number") input.attr({ step: f.step ?? "any", ...(f.min != null ? { min: f.min } : {}) });
      wrapper.append(el("label", f.label, { for: id }), input); if (f.help) wrapper.append(el("p", f.help, { class: "small muted" })); grid.append(wrapper);
    });
    form.append(grid, el("div", "", { class: "form-actions" }).append(button("Annuler", () => jQuery("#editor")[0].close()), el("button", "Enregistrer", { type: "submit", class: "primary" })));
    form.on("submit", event => { event.preventDefault(); App.perform(async () => {
      const values = Object.fromEntries(new FormData(form[0]));
      fields.filter(f => f.type === "number").forEach(f => { values[f.name] = values[f.name] === "" ? null : Number(values[f.name]); });
      await save(values); jQuery("#editor")[0].close();
    }); });
    jQuery("#editor-body").empty().append(form); jQuery("#editor")[0].showModal();
    return form;
  }
  function stat(label, value) { return el("div").append(el("span", label, { class: "stat-label" }), el("span", value, { class: "stat-value" })); }
  return { el, button, number, date, statuses, sex, badge, heading, table, pager, input, select, searchForm, modal, stat };
})();
window.App = {
  session: null, busy: false, expiryTimer: null, pages: {},
  has(...roles) { return !!this.session?.roles.some(r => roles.includes(r)); },
  async perform(action) {
    if (this.busy) return;
    this.busy = true;
    const enabled = jQuery("button:enabled").prop("disabled", true);
    jQuery("#notice,#editor-error,#login-message").empty();
    try { await action(); } catch (error) {
      if (!error.cancelled) {
        const target = !this.session ? "#login-message" : jQuery("#editor")[0].open ? "#editor-error" : "#notice";
        jQuery(target).text(error.message || "L’opération n’a pas pu aboutir.");
      }
    } finally { this.busy = false; enabled.filter((_, node) => node.isConnected).prop("disabled", false); }
  },
  async open(page = "requests", argument) {
    Api.cancelReads();
    jQuery("#notice").empty();
    jQuery("#page").empty().append(Ui.el("div", "Chargement des données…", { class: "loading", role: "status" }));
    jQuery("nav button").removeClass("active").filter('[data-page="' + (page.startsWith("request") ? "requests" : page === "history" ? "patients" : page) + '"]').addClass("active");
    history.replaceState(null, "", "#" + page);
    if (!this.session) return;
    const render = this.pages[page];
    if (!render) throw new Error("Page non disponible.");
    await render(argument);
  },
  showLogin(message = "") {
    clearTimeout(this.expiryTimer); Api.reset(); this.session = null;
    jQuery("#page,#notice,#editor-body,#session-user,#session-roles,#session-expiry").empty();
    if (jQuery("#editor")[0].open) jQuery("#editor")[0].close();
    jQuery("#workspace").prop("hidden", true); jQuery("#login-screen").prop("hidden", false);
    jQuery("#password").val(""); jQuery("#login-message").text(message);
  },
  async signedIn(session) {
    this.session = session;
    jQuery('[data-page="dashboard"]').prop("hidden", !this.has("Biologist"));
    jQuery("#login-screen").prop("hidden", true); jQuery("#workspace").prop("hidden", false);
    jQuery("#session-user").text(session.userName); jQuery("#session-roles").text(session.roles.join(" · "));
    jQuery("#session-expiry").text("Expiration : " + Ui.date(session.expiresAt) + " UTC");
    clearTimeout(this.expiryTimer);
    this.expiryTimer = setTimeout(() => this.showLogin("Session expirée. Reconnectez-vous."), Math.max(0, new Date(session.expiresAt) - Date.now()));
    await this.open("requests");
  }
};
jQuery(async () => {
  jQuery("nav button").on("click", function () { App.perform(() => App.open(this.dataset.page)); });
  jQuery("#close-editor").on("click", () => { if (!App.busy) jQuery("#editor")[0].close(); });
  jQuery("#editor").on("cancel", event => { if (App.busy) event.preventDefault(); });
  jQuery("#login-form").on("submit", event => { event.preventDefault(); App.perform(async () => {
    try {
      await Api.token();
      const session = await Api.write("POST", "/api/auth/login", { userName: jQuery("#username").val(), password: jQuery("#password").val() });
      await Api.token(); // The authenticated identity needs a NEW antiforgery token.
      await App.signedIn(session);
    } finally { jQuery("#password").val(""); }
  }); });
  jQuery("#logout").on("click", () => App.perform(async () => {
    try { await Api.write("POST", "/api/auth/logout", {}); App.showLogin("Vous êtes déconnecté."); }
    catch (error) { App.showLogin("Session locale fermée. La révocation serveur n’a pas pu être confirmée."); throw error; }
  }));
  document.addEventListener("clinisys:expired", () => App.showLogin("Votre session a expiré. Reconnectez-vous."));
  if (location.protocol !== "https:") { jQuery("#login-message").text("Ouvrez cette interface sur l’origine HTTPS de l’API pour utiliser les cookies sécurisés."); return; }
  await App.perform(async () => {
    try { const session = await Api.get("/api/auth/session"); await App.signedIn(session); }
    catch (error) { if (error.status !== 401) throw error; }
  });
});
