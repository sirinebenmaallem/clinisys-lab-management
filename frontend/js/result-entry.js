/* global Ui */
"use strict";
const ResultEntry = (() => {
  function decimal(raw) {
    const match = /^([+-]?)(\d*)(?:\.(\d{0,6}))?$/.exec(String(raw).trim());
    if (!match || !(match[2] || match[3])) return null;
    const value = (BigInt(match[2]) * 1000000n + BigInt((match[3] || "").padEnd(6, "0"))) * (match[1] === "-" ? -1n : 1n);
    return value >= -999999999999999999n && value <= 999999999999999999n ? value : null;
  }
  function evaluate(raw, reference) {
    const value = decimal(raw);
    // The current write API accepts a JSON number. Reject values that would lose
    // precision in that transport rather than previewing a different saved value.
    if (value === null || decimal(String(Number(raw))) !== value) return "Invalid";
    const lower = decimal(reference?.lowerBound), upper = decimal(reference?.upperBound);
    if (reference?.status !== "Applicable" || lower === null || upper === null || lower > upper) return "NotEvaluated";
    return value < lower ? "Low" : value > upper ? "High" : "Normal";
  }
  function attach(form, reference) {
    const input = form.find('[name="value"]');
    const description = reference?.status === "Applicable"
      ? `Référence : ${reference.lowerBound} à ${reference.upperBound} (bornes incluses).`
      : reference?.status === "Unavailable"
        ? "Référence indisponible : unité incompatible ou références ambiguës. Enregistrement soumis au contrôle serveur."
        : "Aucune référence applicable : résultat non évalué.";
    const help = Ui.el("p", description, { id: "result-reference", class: "small" });
    const status = Ui.el("p", "", { id: "result-preview", role: "status", "aria-live": "polite", "aria-atomic": "true" });
    input.attr("aria-describedby", "result-reference result-preview");
    input.after(help, Ui.el("p", "Référence selon le sexe et l’âge figés à l’enregistrement ; pour une correction, référence conservée à la première saisie. Aperçu indicatif, confirmé par le serveur.", { class: "small muted" }), status);
    function update() {
      const state = evaluate(input.val(), reference);
      input[0].setCustomValidity(state === "Invalid" ? "Saisissez une valeur décimale valide, au maximum six décimales, représentable sans perte de précision." : "");
      input.attr("aria-invalid", state === "Invalid" ? "true" : "false");
      const labels = { Invalid: "Valeur invalide — non évaluée", NotEvaluated: "Non évaluée", Low: "Basse", Normal: "Normale", High: "Haute" };
      status.attr("class", "result-evaluation " + state).text(labels[state]);
    }
    input.on("input", update);
    update();
  }
  return { evaluate, attach };
})();
if (typeof module !== "undefined") module.exports = ResultEntry;
