using Kubernator.Core.Generation.Emitters;

namespace Kubernator.Core.Tls.Rotation;

internal static class TlsRotationEmitter
{
    public static string ServiceAccountYaml(TlsRotationOptions options, string saName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: v1");
        w.Line("kind: ServiceAccount");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(saName)}");
        w.Line($"namespace: {YamlValue.String(options.Namespace)}");
        w.Line("labels:");
        w.Indent();
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Line("app.kubernetes.io/component: tls-rotation");
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    public static string RoleYaml(TlsRotationOptions options, string saName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: rbac.authorization.k8s.io/v1");
        w.Line("kind: Role");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(saName)}");
        w.Line($"namespace: {YamlValue.String(options.Namespace)}");
        w.Outdent();
        w.Line("rules:");
        w.Line("  - apiGroups: [\"\"]");
        w.Line("    resources: [\"secrets\"]");
        w.Line($"    resourceNames: [{YamlValue.String(options.SecretName)}]");
        w.Line("    verbs: [\"get\", \"update\", \"patch\"]");
        w.Line("  - apiGroups: [\"\"]");
        w.Line("    resources: [\"secrets\"]");
        w.Line("    verbs: [\"create\"]");
        return w.ToString();
    }

    public static string RoleBindingYaml(TlsRotationOptions options, string saName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: rbac.authorization.k8s.io/v1");
        w.Line("kind: RoleBinding");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(saName)}");
        w.Line($"namespace: {YamlValue.String(options.Namespace)}");
        w.Outdent();
        w.Line("subjects:");
        w.Line("  - kind: ServiceAccount");
        w.Line($"    name: {YamlValue.String(saName)}");
        w.Line($"    namespace: {YamlValue.String(options.Namespace)}");
        w.Line("roleRef:");
        w.Indent();
        w.Line("apiGroup: rbac.authorization.k8s.io");
        w.Line("kind: Role");
        w.Line($"name: {YamlValue.String(saName)}");
        w.Outdent();
        return w.ToString();
    }

    public static string CronJobYaml(TlsRotationOptions options, string cronName, string saName)
    {
        var w = new IndentedTextWriter();
        w.Line("apiVersion: batch/v1");
        w.Line("kind: CronJob");
        w.Line("metadata:");
        w.Indent();
        w.Line($"name: {YamlValue.String(cronName)}");
        w.Line($"namespace: {YamlValue.String(options.Namespace)}");
        w.Line("labels:");
        w.Indent();
        w.Line("app.kubernetes.io/managed-by: kubernator");
        w.Line("app.kubernetes.io/component: tls-rotation");
        w.Outdent();
        w.Outdent();

        w.Line("spec:");
        w.Indent();
        w.Line($"schedule: {YamlValue.String(options.Schedule)}");
        w.Line("concurrencyPolicy: Forbid");
        w.Line($"successfulJobsHistoryLimit: {options.SuccessfulJobsHistoryLimit}");
        w.Line($"failedJobsHistoryLimit: {options.FailedJobsHistoryLimit}");
        w.Line("jobTemplate:");
        w.Indent();
        w.Line("spec:");
        w.Indent();
        w.Line("backoffLimit: 2");
        w.Line("template:");
        w.Indent();
        w.Line("spec:");
        w.Indent();
        w.Line("restartPolicy: OnFailure");
        w.Line($"serviceAccountName: {YamlValue.String(saName)}");
        w.Line("automountServiceAccountToken: true");
        w.Line("securityContext:");
        w.Indent();
        w.Line("runAsNonRoot: true");
        w.Line("runAsUser: 65532");
        w.Line("runAsGroup: 65532");
        w.Line("seccompProfile:");
        w.Indent();
        w.Line("type: RuntimeDefault");
        w.Outdent();
        w.Outdent();
        w.Line("containers:");
        w.Line("  - name: rotate");
        w.Indent();
        w.Indent();
        w.Line($"image: {YamlValue.String(options.Image)}");
        w.Line("imagePullPolicy: IfNotPresent");
        w.Line("securityContext:");
        w.Indent();
        w.Line("allowPrivilegeEscalation: false");
        w.Line("readOnlyRootFilesystem: true");
        w.Line("capabilities:");
        w.Indent();
        w.Line("drop: [\"ALL\"]");
        w.Outdent();
        w.Outdent();
        w.Line("env:");
        w.Line($"  - name: SECRET_NAME");
        w.Line($"    value: {YamlValue.String(options.SecretName)}");
        w.Line("  - name: NAMESPACE");
        w.Line("    valueFrom:");
        w.Line("      fieldRef:");
        w.Line("        fieldPath: metadata.namespace");
        w.Line("  - name: HOSTNAME");
        w.Line($"    value: {YamlValue.String(options.Hostname)}");
        w.Line("  - name: DAYS_VALID");
        w.Line($"    value: {YamlValue.String(options.DaysValid.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
        w.Line("  - name: SAN_LIST");
        w.Line($"    value: {YamlValue.String(BuildSanList(options))}");
        w.Line("resources:");
        w.Indent();
        w.Line("requests:");
        w.Indent();
        w.Line("cpu: 50m");
        w.Line("memory: 64Mi");
        w.Outdent();
        w.Line("limits:");
        w.Indent();
        w.Line("cpu: 200m");
        w.Line("memory: 128Mi");
        w.Outdent();
        w.Outdent();
        w.Line("volumeMounts:");
        w.Line("  - name: workdir");
        w.Line("    mountPath: /work");
        w.Line("command: [\"/bin/sh\", \"-c\"]");
        w.Line("args:");
        w.Line("  - |");
        EmitRotationScript(w, indentSpaces: 6);
        w.Outdent();
        w.Outdent();

        w.Line("volumes:");
        w.Line("  - name: workdir");
        w.Line("    emptyDir:");
        w.Line("      medium: Memory");
        w.Outdent();
        w.Outdent();
        w.Outdent();
        w.Outdent();
        return w.ToString();
    }

    private static string BuildSanList(TlsRotationOptions options)
    {
        var sans = new List<string> { options.Hostname };
        sans.AddRange(options.AdditionalHostnames);
        return string.Join(",", sans);
    }

    private static void EmitRotationScript(IndentedTextWriter w, int indentSpaces)
    {
        var prefix = new string(' ', indentSpaces);
        foreach (var line in RotationScriptLines)
        {
            w.Line(prefix + line);
        }
    }

    private static readonly string[] RotationScriptLines =
    [
        "set -eu",
        "cd /work",
        "echo \"[rotate] generating cert for ${HOSTNAME} (${DAYS_VALID} days)\"",
        "SAN_CONFIG=$(printf '[req]\\ndistinguished_name=req\\n[san]\\nsubjectAltName=%s\\n' \"$(echo \"$SAN_LIST\" | awk -F, '{out=\"\"; for(i=1;i<=NF;i++){out=out \"DNS:\" $i (i<NF?\",\":\"\")}; print out}')\")",
        "echo \"$SAN_CONFIG\" > openssl.cnf",
        "openssl req -x509 -newkey rsa:2048 -nodes \\",
        "  -keyout tls.key -out tls.crt \\",
        "  -days ${DAYS_VALID} -subj \"/CN=${HOSTNAME}\" \\",
        "  -reqexts san -extensions san -config openssl.cnf",
        "CRT=$(base64 -w0 tls.crt)",
        "KEY=$(base64 -w0 tls.key)",
        "TOKEN=$(cat /var/run/secrets/kubernetes.io/serviceaccount/token)",
        "API=https://kubernetes.default.svc",
        "CACERT=/var/run/secrets/kubernetes.io/serviceaccount/ca.crt",
        "BODY=$(printf '{\"apiVersion\":\"v1\",\"kind\":\"Secret\",\"type\":\"kubernetes.io/tls\",\"metadata\":{\"name\":\"%s\",\"namespace\":\"%s\"},\"data\":{\"tls.crt\":\"%s\",\"tls.key\":\"%s\"}}' \"$SECRET_NAME\" \"$NAMESPACE\" \"$CRT\" \"$KEY\")",
        "URL=$API/api/v1/namespaces/$NAMESPACE/secrets/$SECRET_NAME",
        "echo \"[rotate] PUT $URL\"",
        "STATUS=$(curl -sS --cacert \"$CACERT\" -H \"Authorization: Bearer $TOKEN\" -H \"Content-Type: application/json\" -X PUT --data \"$BODY\" -o response.json -w \"%{http_code}\" \"$URL\" || echo failure)",
        "if [ \"$STATUS\" = \"404\" ]; then",
        "  echo \"[rotate] secret missing, creating\"",
        "  CREATE_URL=$API/api/v1/namespaces/$NAMESPACE/secrets",
        "  STATUS=$(curl -sS --cacert \"$CACERT\" -H \"Authorization: Bearer $TOKEN\" -H \"Content-Type: application/json\" -X POST --data \"$BODY\" -o response.json -w \"%{http_code}\" \"$CREATE_URL\" || echo failure)",
        "fi",
        "if [ \"$STATUS\" != \"200\" ] && [ \"$STATUS\" != \"201\" ]; then",
        "  echo \"[rotate] FAILED status=$STATUS\"",
        "  cat response.json",
        "  exit 1",
        "fi",
        "echo \"[rotate] OK status=$STATUS\""
    ];
}
