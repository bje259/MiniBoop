/**
	{
		"api":1,
		"name":"Regex Substitution",
		"description":"Search for a regex pattern in the text, then replace it.",
		"author":"bje",
		"icon":"elephant",
		"tags":"regex,search,substitution"
	}
**/

function main(state) {
  try {
    const lines = state.text.split(/\r?\n/);

    // Matches: s<delimiter><pattern><delimiter><replacement><delimiter><flags>
    const rule = /^s(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<rep>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>[a-z]*)$/i;

    const substitutions = [];
    let firstNonRuleLineIndex = lines.length;

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();
      const m = rule.exec(line);
      if (!m) {
        firstNonRuleLineIndex = i;
        break;
      }

      let { pat, rep, flags } = m.groups;

      // Handle common escape sequences in replacement
      rep = rep
        .replace(/\\n/g, "\n")
        .replace(/\\r/g, "\r")
        .replace(/\\t/g, "\t")
        .replace(/\\\\/g, "\\")
        .replace(/\\\$/g, "$");

      substitutions.push({
        rx: new RegExp(pat, flags || undefined),
        rep
      });
    }

    // Remaining text to apply substitutions on
    const textBody = lines.slice(firstNonRuleLineIndex).join("\n");

    let result = textBody;
    for (const { rx, rep } of substitutions) {
      result = result.replace(rx, rep);
    }

    state.text = result;
  } catch (err) {
    state.postError("Regex substitution failed.");
    state.text += "\n" + err.message;
  }
}
