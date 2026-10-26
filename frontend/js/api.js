/* global jQuery */
"use strict";
window.Api = (() => {
  let csrf = null, epoch = 0;
  const reads = new Set();
  function problem(status, body) {
    const defaults = { 0: "Le serveur est inaccessible. Vérifiez la connexion HTTPS.", 400: "Vérifiez les informations saisies.", 401: "Votre session a expiré. Reconnectez-vous.", 403: "Votre rôle ne permet pas cette opération.", 404: "La ressource n’existe pas ou n’est plus disponible.", 409: "Les données ou le statut ont changé. Actualisez avant de réessayer.", 429: "Trop de tentatives. Réessayez plus tard.", 503: "Le service est temporairement indisponible." };
    let message = defaults[status] || "L’opération n’a pas pu aboutir.";
    if (body?.outcomeUnknown) message = "Le résultat de la transaction est incertain. Ne renvoyez pas la saisie : actualisez et vérifiez les données avant toute nouvelle tentative.";
    else if (status === 400 && body?.errors) message += "\n" + Object.values(body.errors).flat().join("\n");
    return Object.assign(new Error(message), { status, outcomeUnknown: !!body?.outcomeUnknown });
  }
  function send(method, path, data) {
    if (!path.startsWith("/api/")) return Promise.reject(new Error("Route API non autorisée."));
    const generation = epoch;
    return new Promise((resolve, reject) => {
      const xhr = jQuery.ajax({ url: path, method, timeout: 30000, data: data === undefined ? undefined : JSON.stringify(data), contentType: "application/json", dataType: "json", cache: false, headers: !["GET", "HEAD"].includes(method) && csrf ? { [csrf.headerName]: csrf.requestToken } : {} });
      if (method === "GET") reads.add(xhr);
      xhr.done(value => generation === epoch ? resolve(value) : reject({ cancelled: true }));
      xhr.fail((response, textStatus) => {
        if (textStatus === "abort" || generation !== epoch) return reject({ cancelled: true });
        const error = problem(response.status, response.status === 0 && method !== "GET" ? { outcomeUnknown: true } : response.responseJSON);
        if (response.status === 401 && path === "/api/auth/login") error.message = "Identifiants incorrects ou accès indisponible.";
        if (response.status === 401 && !path.startsWith("/api/auth/")) document.dispatchEvent(new Event("clinisys:expired"));
        reject(error);
      });
      xhr.always(() => reads.delete(xhr));
    });
  }
  async function token() { csrf = await send("GET", "/api/auth/antiforgery"); }
  async function write(method, path, data) { if (!csrf) await token(); return send(method, path, data); }
  function cancelReads() { for (const xhr of reads) xhr.abort(); reads.clear(); }
  function reset() { epoch++; csrf = null; cancelReads(); }
  async function download(id) {
    const generation = epoch;
    const response = await fetch("/api/laboratory-requests/" + encodeURIComponent(id) + "/report", { credentials: "same-origin", cache: "no-store" });
    if (!response.ok) {
      const body = await response.json().catch(() => null);
      if (response.status === 401) document.dispatchEvent(new Event("clinisys:expired"));
      throw problem(response.status, body);
    }
    const blob = await response.blob();
    if (generation !== epoch) throw { cancelled: true };
    if (!response.headers.get("Content-Type")?.startsWith("application/pdf")) throw new Error("Le serveur n’a pas retourné de PDF.");
    const disposition = response.headers.get("Content-Disposition") || "";
    const match = /filename\*?=(?:UTF-8''|\")?([^;\"]+)/i.exec(disposition);
    let name = "compte-rendu.pdf";
    if (match) { try { const candidate = decodeURIComponent(match[1]); if (/^[a-zA-Z0-9_.-]+\.pdf$/.test(candidate)) name = candidate; } catch { /* use safe fallback */ } }
    const url = URL.createObjectURL(blob), link = document.createElement("a");
    link.href = url; link.download = name; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return { get: (path, query) => send("GET", path + (query ? "?" + jQuery.param(query) : "")), write, token, reset, cancelReads, download };
})();
