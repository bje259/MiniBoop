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


const DEBUG = false;

function mainold(state) {
    const nestedRegex = /<(?<nflags>[sd]{0,2})<<(?<inner>.*)>(?:\((?<rawloopvals>(?:\S+(?<!\\),?)*?)\)>(?<rawloopvar>[A-Za-z0-9_;]+)>|>>)/gs;
    const definitions = {};
    const substitutions = [];
    state.nflags = "";
    try {
        const initInput = state.text
        const nestedCheckResults = initInput.replaceAll(nestedRegex, (m, ...args) => {
            const { inner, nflags, rawloopvals, rawloopvar } = args.slice(-1)[0];
            if (rawloopvals && rawloopvar) {
                const varParts = rawloopvar.split(/(?<!\\);/).map(ele => dslUnescapeold(ele.trim()));
                const rawvals = rawloopvals.split(/(?<!\\),/).map(ele=> ele.trim());
                let combinedResult = "";
                for (const rawval of rawvals) {
                    const valParts = rawval.split(/(?<!\\);/).map(ele => dslUnescapeold(ele.trim()));
                    if (valParts.length !== varParts.length) {
                        const errorText = `Loop variable count (${varParts.length}) items: ${varParts.join(" :: ")} does not match value count (${valParts.length}) items: ${valParts.join(" :: ")}`;
                        combinedResult += errorText;
                        state.postError(errorText);
                        continue;
                    }
                    const varPairs = valParts.map((v, i) => [varParts[i], v]);
                    let replacedInner = inner;
                    for (const [varkey, varval] of varPairs) {
                        replacedInner = replacedInner.replaceAll(new RegExp(`{${varkey}}`, "g"), varval);
                    }
                    const newState = { text: replacedInner, postError: state.postError };
                    main(newState);
                    combinedResult += newState.text;
                }
                Object.assign(state, { nflags: nflags });
                return combinedResult;
            }
            const newState = { text: inner, postError: state.postError, nested: true };
            main(newState);
            Object.assign(state, { nflags: nflags, definitions: newState.definitions, substitutions: newState.substitutions });
            return newState.text;
        });
        state.text = nestedCheckResults;
        if (state.nflags.includes("d")) Object.assign(definitions, state.definitions);
        if (state.nflags.includes("s")) substitutions.push(...state.substitutions);
    } catch (err) {
        state.postError("Regex substitution failed.");
        state.text += "\n" + err.message;
    }
    try {
        const lines = state.text.split(/\r?\n/);
        // lines.push("EOF");
        const ruleRegex =
            /^s(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<rep>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>[a-z]*)$/i;

        const defRegex =
            /^def\s+(?<id>[A-Za-z][A-Za-z0-9_]*)\s*(?<d>[^A-Za-z0-9\s])(?<val>(?:\\.|(?!\k<d>).)*)\k<d>(?:$|\r?\n)/s;


        const headers = [];

        let firstNonRuleLineIndex = lines.length;

        // --- Parse definitions and rules ---
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i].trim();
            // line = expand(line, definitions);
            
            // --- NEW: skip blank lines while parsing rules/defs
            if (line === "") {
                continue;
            }
            if (line.startsWith("//")) {
                continue;
            }
            const remainingLines = lines.slice(i).join("\n");
            const defMatch = defRegex.exec(remainingLines);
            if (defMatch) {
                headers.push(expand(defMatch[0].trim(), definitions));
                // headers.push(line);
                const { id, val } = defMatch.groups;
                definitions[id] = expand(val, definitions);
                // definitions[id] = val;
                const defLineCount = defMatch[0].trim().split(/\r?\n/).length;
                i += defLineCount - 1;
                continue;
            }

            const ruleMatch = ruleRegex.exec(line);
            if (ruleMatch) {
                headers.push(expand(line, definitions));
                let { d, pat, rep, flags } = ruleMatch.groups;
                pat = expand(pat, definitions);
                rep = expand(rep, definitions);
                flags = expand(flags, definitions);

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

                continue;
            }

            // First non-rule, non-def line → body begins
            firstNonRuleLineIndex = i;
            break;
        }

        // --- The body of text to transform ---
        let textBody = lines.slice(firstNonRuleLineIndex).join("\n");

        // --- Apply substitutions ---
        const runDebug = (initInput,finalOutput) => {
            let debugTxt = "\n";
            // debugTxt += "\n===\n" + "Initial Input:\n" + initInput;
            debugTxt += "\n===\n" + "After Nested Processing:\n" + finalOutput;
            debugTxt += "\n===\n" + JSON.stringify(definitions);
            debugTxt += "\n===\n" + JSON.stringify(substitutions.map(ele=>{return {...ele,...{rx: ele.rx.toString()}}}));
            debugTxt += "\n===\n" + "FirstNonRuleLineIndex: " + firstNonRuleLineIndex + "\n";
            debugTxt = "\n"+debugTxt.split("\n").map(lines=>"// "+lines).join("\n");
            return finalOutput += debugTxt;
        }
        // substitutions.push({
        //     rx: /EOF/g,
        //     rep: ""
        // });
        if (substitutions.length === 0 && !state.retry) {
            const initInput = state.text;
            textBody = expand(textBody, definitions);
            const newState = { text: textBody, postError: state.postError, retry: true};
            main(newState);
            if (DEBUG) newState.text = runDebug(initInput, newState.text);
            if (state.nested) {
                state.text = newState.text;
            } else {
                state.text = headers.join("\n") + "\n" + newState.text;
            }
            state.definitions = definitions;
            state.substitutions = substitutions;
            return;
        }
        const runSubstitutions = (inputText, subs) => {
            let outputText = inputText;
            for (const { rx, rep } of subs) {
                outputText = outputText.replace(rx, rep);
            }
            return outputText;
        };
        const initInput = state.text;
        let finalOutput = runSubstitutions(textBody, substitutions);
        if (DEBUG) finalOutput = runDebug(initInput, finalOutput);
        if (state.nested) {
            state.text = finalOutput;
        } else {
            state.text = headers.join("\n") + "\n" + finalOutput;
        }
        state.definitions = definitions;
        state.substitutions = substitutions;
    } catch (err) {
        state.postError("Regex substitution failed.");
        state.text += "\n" + err.message;
    }
}

function debugStringify(obj) {
  const seen = new WeakSet();

  return JSON.stringify(
    obj,
    (key, value) => {
      // Functions
      if (typeof value === "function") {
        return `[Function ${value.name || "anonymous"}]`;
      }

      // BigInt
      if (typeof value === "bigint") {
        return `${value}n`;
      }

      // Symbol
      if (typeof value === "symbol") {
        return value.toString();
      }

      // Errors
      if (value instanceof Error) {
        return {
          name: value.name,
          message: value.message,
          stack: value.stack,
        };
      }

      // Dates
      if (value instanceof Date) {
        return value.toISOString();
      }

      // Map
      if (value instanceof Map) {
        return Object.fromEntries(value);
      }

      // Set
      if (value instanceof Set) {
        return Array.from(value);
      }

      // Truncate huge arrays
      if (Array.isArray(value) && value.length > 100) {
        return [
          ...value.slice(0, 100),
          `...(${value.length - 100} more items)`,
        ];
      }

      // Circular refs
      if (typeof value === "object" && value !== null) {
        if (seen.has(value)) return "[Circular]";
        seen.add(value);
      }

      return value;
    },
    2
  );
}

const expansionLog = [];
// Macro expander (simple literal replace)
function expand(str, defs) {
    let last;
    let safety = 0;

    do {
        last = str;
        for (const id in defs) {
            str = str.replaceAll(new RegExp(`\\$\\{${id}\\}`, "g"), (m) => {
                expansionLog.push({ id, value: defs[id] });
                return defs[id];
            });
        }
        safety++;
        if (safety > 50) break;  // prevent runaway recursion
    } while (str !== last);

    return str;
}

function expandReplacement(rep, m) {
    try {
        let result = rep
        // $& → full match
        .replace(/\$&/g, m[0])

        // $1, $2...
        .replace(/\$([0-9]+)/g, (_, i) => m[i] ?? "")

        // $<name>
        .replace(/\$<([^>]+)>/g, (_, name) => m.groups?.[name] ?? "")

        // $` → text before match
        .replace(/\$`/g, m.input.slice(0, m.index))

        // $' → text after match
        .replace(/\$'/g, m.input.slice(m.index + m[0].length))

        // $$ → $
        .replace(/\$\$/g, "$");
        return result;
    } catch (err) {
        throw new Error(
            `<<Error in replacement: ${err.message}
            Replacement string: ${rep}
            Captures: ${JSON.stringify(m)}
            Input: ${m.input}
            Index: ${m.index}
            Groups: ${JSON.stringify(m.groups)}>>`
        );

    }
}


function dslUnescapeold(str) {
    return str
        //.replace(/\\\\/g, "\\")   // \\ → \
        .replace(/\\;/g, ";")     // \; → ;
        .replace(/\\,/g, ",")     // \, → ,
        .replace(/\\\(/g, "(")    // \( → (
        .replace(/\\\)/g, ")")    // \) → )
}

function unescapeDSL(str) {
    let result = "";
    let escaping = false;

    for (let i = 0; i < str.length; i++) {
        const c = str[i];

        if (escaping) {
            switch (c) {
                case "n": result += "\n"; break;
                case "r": result += "\r"; break;
                case "t": result += "\t"; break;
                case "\\": result += "\\"; break;
                case "$": result += "$"; break;
                default: result += c; break;
            }
            escaping = false;
        } else if (c === "\\") {
            escaping = true;
        } else {
            result += c;
        }
    }

    return result;
}
function formatter(matchAry, fmtStr) {
  return matchAry.map(m => {
    fmtStr = fmtStr.replace(/\\n/g, "\n")
                    .replace(/\\r/g, "\r")
                    .replace(/\\t/g, "\t")
                    .replace(/\\\\/g, "\\");
    const chunks = fmtStr.split(/(\?)/);
    const result = [];
    let optFlag = false;

    for (const chunk of chunks) {
      if (chunk === "?") {
        optFlag = !optFlag;
        continue;
      }

      let foundMissing = false;
      // Replace $<input> with the entire match
      let text = chunk;
      text = text.replace(/(?<!\\)\$<input>/g, m.input);
      
      // Replace numbered groups: $1, $2...
      text = text.replace(/(?<!\\)\$([0-9]+)/g, (_, i) => {
        const repVal = m[i];
        if (repVal === undefined) foundMissing = true;
        return repVal ?? "";
      });

      // Replace named groups: $<name>
      text = text.replace(/(?<!\\)\$<([^>]+)>/g, (_, name) => {
        const repVal = m.groups?.[name];
        if (repVal === undefined) foundMissing = true;
        return repVal ?? "";
      });


      // Unescape literal \$ → $
      text = text.replace(/\\\$/g, "$");

      // Emit chunk unless optional block failed
      if (!optFlag || !foundMissing) {
        result.push(text);
      }
    }

    return result.join("")
      .split("\n")
      .filter(Boolean)
      .join("\n");

  }).join("\n\n");
}

function loadBuiltinDefinitions(defs) {
    const builtinJson = (String.raw`
    {
        "removeBlankLines": "mli/^$/gm",
        "rbl": "mli/^$/gm",
        "ialphnum": "[a-zA-Z0-9]",
        "lalphnum": "[a-z0-9]",
        "anychar" : "[\s\S]",
        "lineEnding" : "(?:\r?\n)",
        "le": "(?:\r?\n)",
        "devtoolCpCleanup" : "s/\n\:\s*\n/:/g",
    }`).replace(/\\/g,String.raw`\\`);
    try {
        const builtinDefs = JSON.parse(builtinJson);
        Object.assign(defs, builtinDefs);
    } catch (err) {
        console.error("Failed to load builtin definitions:", err);
    }
}

function consumeStateJson(state) {
    try {
        const stateBlockRE = /beginjson\r?\n(?<jsonBody>[\s\S]*?)\r?\nendjson/;
        state.text = state.text.replace(stateBlockRE, (m, ...args) => {
            const hasNamedGroups = typeof args.at(-1) === "object";
            const offset = hasNamedGroups ? args.at(-3) : args.at(-2);
            const { jsonBody } = args.at(-1);
            const parsedState = JSON.parse(jsonBody);
            state.data = parsedState;
            return "";
        });
    } catch (err) {
        console.error("Failed to consume state JSON:", err);
    }
}

function writeStateJson(state) {
    try {        
        if (state.data !== undefined) {
            const jsonState = JSON.stringify(state.data, null, 2);
            state.text = `beginjson\n${jsonState}\nendjson` + "\n\n" + state.text;
        }
    } catch (err) {
        console.error("Failed to write state JSON:", err);
    }
}

function main(state) {
    const definitions = {};
    loadBuiltinDefinitions(definitions);
    const rules = [];
    const matchRules = rules;
    const substitutions = rules;
    state.nflags = "";
    const definitionInputLines = [];
    consumeStateJson(state);
    const initInput = state.text;

    const ruleLog = [];

    function addSubRule(rx, rep) {
        const ctx = {rules, rx, rep, addSubRule, addMatchRule, definitions, state};
        // if (definitions.eachSubDef) definitions.eachSubDef(ctx);
        ({rx, rep} = ctx);
        rules.push({
            type: "sub",
            rx,
            rep,
            fn: (text) => {
                return text.replace(rx, (...args) => {
                    const hasNamedGroups = typeof args.at(-1) === "object";

                    const groups = hasNamedGroups ? args.at(-1) : undefined;
                    const input  = hasNamedGroups ? args.at(-2) : args.at(-1);
                    const offset = hasNamedGroups ? args.at(-3) : args.at(-2);

                    const m = args.slice(0, hasNamedGroups ? -3 : -2);

                    m.input = input;
                    m.index = offset;
                    m.groups = groups;

                    if (definitions.eachSubFn) definitions.eachSubFn(ctx, text);

                    const result = expandReplacement(rep, m);

                    ruleLog.push({ rx: rx.toString(), rep, match: result, captures: m });


                    return result;
                })
            }
        });
    }

    function addMatchRule(rx, addFn, mode="extract") {
        rules.push({
            type: "match",
            rx,
            mode,
            fn(text) {
                ruleLog.push({ rx: rx.toString(), mode, match: text.match(rx) });
                return addFn(text, rx);
            }
        });
    }
    let headers = []
    state.headers = headers;

    try {
        /**
         * @type {string}
         */
        let inputText = state.text;
        const definitionBlockRegex = /(?:(?<=\n)|^)beginDefs(?<defBody>.*)endDefs/gs;
        let defBlkMtch = false;
        let startIdx = 0;
        // let test = '';
        inputText = inputText.replace(definitionBlockRegex, (m, ...args) => {
            const hasNamedGroups = typeof args.at(-1) === "object";
            const offset = hasNamedGroups ? args.at(-3) : args.at(-2);
            startIdx += offset;
            defBlkMtch = true;
            const { defBody } = args.at(-1);
            definitionInputLines.push({defBody, rawDefInput: m});
            // test = JSON.stringify({defBody, rawDefInput: m});
            return "";
        });
        //const defBlockMatch = definitionBlockRegex.exec(inputText);
        if (defBlkMtch) {
            const defBody = definitionInputLines.map(ele=>ele.defBody).join("\n");
            const defFn = new Function(defBody);
            defFn.call(definitions);
            Object.defineProperty(definitions,"defBody",{value: defBody, writable: false, enumerable: false}); 
            // state.text = definitionInputLines.map(ele=>ele.rawDefInput).join("\n") + "\n" + initInput;
            // state.text = initInput + '\n' + `defFn: ${defFn}\ndefinitions: ${JSON.stringify(definitions,null,2)}`;
            state.text = inputText;
            //initInput.slice(0,startIdx-1).split(/\r?\n/).forEach(line => headers.push(line));
            definitionInputLines.map(ele=>ele.rawDefInput).forEach(line => headers.push(line));
            // headers.push(startIdx);
        } else {
            state.text = initInput;
        }       
            //state.headers = headers;
        // state.text = "checking: " + test + "\n" + state.text;
    } catch (err) {
        state.text += "\nsection1\n" + err.message;
        err.stack && (state.text += "\n" + err.stack);
        state.postError(`Regex substitution failed. ${err.message}`);
        return;
    }
    try {
        const lines = state.text.split(/\r?\n/);
        // lines.push("EOF");
        const subRuleRegex =
            /^s(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<rep>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>[a-z$]*)$/i;

        const defRegex =
            /^def\s+(?<id>[A-Za-z][A-Za-z0-9_]*)\s*(?<d>[^A-Za-z0-9\s])(?<val>(?:\\.|(?!\k<d>).)*)\k<d>(?:$|\r?\n)/s;

        const matchRuleRegex =
            /^m(?<mode>[a-hj-z]*)(?<invert>i?)(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<flags>(?!\k<d>)[a-z$]*)$/i;
        
        const formattedMatchRuleRegex =
            /^m(?<mode>[a-hj-z]*)(?<invert>i?)(?<d>[^A-Za-z0-9\s])(?<pat>(?:\\.|(?!\k<d>).)+)\k<d>(?<fmt>(?:\\.|(?!\k<d>).)*)\k<d>(?<flags>(?!\k<d>)[a-z$]*)$/i;

        const singleDefinitionReceiverRegex =
            /^\$\{(?<id>[A-Za-z][A-Za-z0-9_]*)\}$/;

        let firstNonRuleLineIndex = lines.length;
        
        function extract(text,rx, fmtStr) {
            const out = [];
            let m;
            const rawMatches = [];
            while ((m = rx.exec(text)) !== null) {
                rawMatches.push(m);
                if (!rx.global) break;
            }
            if (fmtStr) {
                return formatter(rawMatches, fmtStr);
            } else {
                for (const match of rawMatches) {
                    out.push(match[0]);
                }
            }
            return out.join("\n");
        }

        function matchLines(text, rx, invert=false, fmtStr) {
            return text.split(/\r?\n/).map(line => {
                const matches = (!rx.global) ? [line.match(rx)] : [...line.matchAll(rx) || []];
                // if (Array.isArray(matches) && matches.length > 0 && matches[0].length === 0) {
                //     matches.map(ele => { /*ele[0] = line*/; return ele; });
                // }
                return { line, matches };
            }).filter(({ line, matches }) => {
                return invert ? !Boolean(matches.length) : Boolean(matches.length);
            }).map(({ line, matches }) => fmtStr ? formatter(matches,fmtStr): line).join("\n");
        }

        const matchOpRegistry = {
            extract,
            matchLines(text, rx, fmtStr) {
                return matchLines(text, rx, false, fmtStr);
            },
            matchLinesInverted(text, rx, fmtStr) {
                return matchLines(text, rx, true, fmtStr);
            },
            matchPrepend(text, rx, fmtStr) {
                const matches = extract(text, rx, fmtStr);
                return matches + "\n\n" + text;
            },
            matchAppend(text, rx, fmtStr) {
                const matches = extract(text, rx, fmtStr);
                return text + "\n\n" + matches;
            },
            extractInverted(text, rx ) {
                return text.replace(rx,"");
            }
        }


        // --- Parse definitions and rules ---
        for (let i = 0; i < lines.length; i++) {
            let line = lines[i].trim();
            // line = expand(line, definitions);
            
            // --- NEW: skip blank lines while parsing rules/defs
            if (line === "") {
                continue;
            }
            if (line.startsWith("//")) {
                continue;
            }
            const remainingLines = lines.slice(i).join("\n");
            const defMatch = defRegex.exec(remainingLines);
            if (defMatch) {
                headers.push(expand(defMatch[0].trim(), definitions));
                // headers.push(line);
                const { id, val } = defMatch.groups;
                definitions[id] = expand(val, definitions);
                // definitions[id] = val;
                const defLineCount = defMatch[0].trim().split(/\r?\n/).length;
                i += defLineCount - 1;
                continue;
            }

            const singleDefMatch = singleDefinitionReceiverRegex.exec(line);
            if (singleDefMatch) {
                line = expand(line, definitions);
            }

            const subRuleMatch = subRuleRegex.exec(line);
            if (subRuleMatch) {
                headers.push(line);
                let { d, pat, rep, flags } = subRuleMatch.groups;
                const defParams = {d, pat, rep, flags};
                const ctx = {rules, defParams, addSubRule, addMatchRule, definitions, state};
                if (definitions.eachSubDef) definitions.eachSubDef(ctx);
                pat = expand(pat, definitions);
                rep = expand(rep, definitions);
                flags = expand(flags, definitions).trim();

                // rep = rep
                //     .replace(/\\n/g, "\n")
                //     .replace(/\\r/g, "\r")
                //     .replace(/\\t/g, "\t")
                //     .replace(/\\\\/g, "\\")
                //     .replace(/\\\$/g, "$");

                rep = unescapeDSL(rep);

                const validFlags =
                [...new Set(flags.replace(/[^gimsuy]/g, ""))].join("");

                addSubRule(new RegExp(pat, validFlags), rep);

                continue;
            }

            const fmtRuleMatch = formattedMatchRuleRegex.exec(line);
            const matchRuleMatch = matchRuleRegex.exec(line);

            if (fmtRuleMatch || matchRuleMatch) {
                const applyFormat = Boolean(fmtRuleMatch);
                //headers.push(expand(line, definitions));
                let { d, mode, invert, pat, fmt, flags } = (matchRuleMatch || fmtRuleMatch).groups;
                const defParams = { d, mode, invert, pat, fmt, flags };
                const ctx = {rules, defParams, addSubRule, addMatchRule, definitions, state};
                if (definitions.eachMatchDef) definitions.eachMatchDef(ctx);
                pat = expand(pat, definitions);
                flags = expand(flags, definitions).trim();
                if (fmt) {
                    headers.push(`m${mode}${invert}/${pat}/${fmt}/${flags}`);
                } else {                    
                    headers.push(`m${mode}${invert}/${pat}/${flags}`);
                }
                fmt = expand(fmt || "", definitions);
                // const addFmt = (fn,re) => {
                //     if (applyFormat && fmt) {
                //         return (outputText) => {
                //             return fn(outputText,re,fmt);
                //         };
                //     } else {
                //         return (outputText) => fn(outputText, re);
                //     }
                // }
                const setMode = (md) => {
                    let modeOp = "extract" + (invert ? "Inverted" : "");
                    let modeFn = invert?matchOpRegistry.extractInverted:matchOpRegistry.extract;
                    if (md.includes("l")) {
                        modeOp = "lines"+ (invert ? "Inverted" : "");
                        modeFn = invert?matchOpRegistry.matchLinesInverted:matchOpRegistry.matchLines;
                        return {modeOp, modeFn};
                    }
                    if (md.includes("a")) {
                        modeOp = "append"+ (invert ? "Inverted" : "");
                        modeFn = invert?matchOpRegistry.matchPrepend:matchOpRegistry.matchAppend;
                        return {modeOp, modeFn};
                    }
                    if (md.includes("p")) {
                        modeOp = "prepend"+ (invert ? "Inverted" : "");
                        modeFn = invert?matchOpRegistry.matchAppend:matchOpRegistry.matchPrepend;
                        return {modeOp, modeFn};
                    }
                    return {modeOp, modeFn};
                };
                let {modeOp, modeFn} = setMode(mode);
                const validFlags =
                [...new Set(flags.replace(/[^gimsuy]/g, ""))].join("");

                const re = new RegExp(pat, validFlags);
                const op = (outputText) => applyFormat?modeFn(outputText, re, expand(fmt, definitions)):modeFn(outputText, re);
                modeOp = modeOp + (applyFormat ? "WithFormat" : "");
                addMatchRule(re, op, modeOp);
                // rules.push({
                //     type: "match",
                //     mode: modeOp,
                //     rx: new RegExp(pat, validFlags || undefined),
                //     fn: modeFn
                // });

                continue;
            }

            // First non-rule, non-def line → body begins
            firstNonRuleLineIndex = i;
            break;
        }

        // --- The body of text to transform ---
        let textBody = lines.slice(firstNonRuleLineIndex).join("\n");

        // --- Apply substitutions ---
        const runDebug = (initInput,finalOutput) => {
            let debugTxt = "\n";
            debugTxt += `\n${"=".repeat(20)}\n` + "Initial Input:\n" + initInput;
            debugTxt += `\n${"=".repeat(20)}\n` + "After Nested Processing:\n" + finalOutput;
            debugTxt += `\n${"=".repeat(20)}\n` + "Definitions:\n" + debugStringify(definitions);
            debugTxt += `\n${"=".repeat(20)}\n` + "DefBody:\n" + definitions.defBody;
            debugTxt += `\n${"=".repeat(20)}\n` + "Rules:\n" + debugStringify(rules.map(ele=>{return {...ele,...{rx: ele.rx.toString()}}}));
            debugTxt += `\n${"=".repeat(20)}\n` + "FirstNonRuleLineIndex: " + firstNonRuleLineIndex + "\n";
            debugTxt += `\n${"=".repeat(20)}\n` + "Headers:\n" + headers.join("\n") + "\n";
            debugTxt += `\n${"=".repeat(20)}\n` + "Expansion Log:\n" + debugStringify(expansionLog) + "\n";
            debugTxt += `\n${"=".repeat(20)}\n` + "Rule Application Log:\n" + debugStringify(ruleLog) + "\n";
            debugTxt = "\n"+debugTxt.split("\n").map(lines=>"// "+lines).join("\n");
            return finalOutput += debugTxt;
        }
        // substitutions.push({
        //     rx: /EOF/g,
        //     rep: ""
        // });

        //run definitions hook
        const ctx = {
            rules,
            addSubRule,
            addMatchRule,
            state,
            definitions,
            textBody
        };
        if (definitions.preSubFn) definitions.preSubFn(ctx);

        if (false && substitutions.length === 0 && !state.retry) {
            const initInput = state.text;
            textBody = expand(textBody, definitions);
            const newState = { text: textBody, postError: state.postError, retry: true};
            main(newState);
            if (DEBUG) newState.text = runDebug(initInput, newState.text);
            if (state.nested) {
                state.text = newState.text;
            } else {
                state.text = headers.join("\n") + "\n" + newState.text;
            }
            state.definitions += definitions;
            state.substitutions += substitutions;
            return;
        }
        
        const runSubstitutions = (inputText, subs) => {
            let outputText = inputText;
            for (const { rx, rep } of subs) {
                outputText = outputText.replace(rx, rep);
            }
            return outputText;
        };

        const runRules = (inputText, rules) => {
            let outputText = inputText;
            for (const {fn} of rules) {
                outputText = fn(outputText);
            }
            return outputText;
        };
        let finalOutput = runRules(textBody, rules);
        if (DEBUG) finalOutput = runDebug(initInput, finalOutput);
        if (state.nested) {
            state.text = finalOutput;
            writeStateJson(state);
        } else {
            state.text = headers.join("\n") + "\n" + finalOutput;
            writeStateJson(state);
        }
        state.definitions = definitions;
        state.substitutions = substitutions;
    } catch (err) {
        state.text += "\nlastsection\n" + err.message;
        err.stack && (state.text += "\n" + err.stack);
        writeStateJson(state);
        state.postError(`Regex substitution failed. ${err.message}`);
    }
}


