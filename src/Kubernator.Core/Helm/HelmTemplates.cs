namespace Kubernator.Core.Helm;

internal static class HelmTemplates
{
    public const string Helpers = """
        {{- define "kubernator.name" -}}
        {{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
        {{- end -}}

        {{- define "kubernator.fullname" -}}
        {{- if .Values.fullnameOverride -}}
        {{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" -}}
        {{- else -}}
        {{- $name := default .Chart.Name .Values.nameOverride -}}
        {{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" -}}
        {{- end -}}
        {{- end -}}

        {{- define "kubernator.labels" -}}
        app.kubernetes.io/name: {{ include "kubernator.name" . }}
        app.kubernetes.io/instance: {{ .Release.Name }}
        app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
        app.kubernetes.io/managed-by: {{ .Release.Service }}
        helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
        {{- end -}}

        {{- define "kubernator.selectorLabels" -}}
        app.kubernetes.io/name: {{ include "kubernator.name" . }}
        app.kubernetes.io/instance: {{ .Release.Name }}
        {{- end -}}
        """;

    public const string Deployment = """
        apiVersion: apps/v1
        kind: Deployment
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          replicas: {{ .Values.replicaCount }}
          revisionHistoryLimit: 3
          selector:
            matchLabels:
              {{- include "kubernator.selectorLabels" . | nindent 6 }}
          strategy:
            type: RollingUpdate
            rollingUpdate:
              maxSurge: 1
              maxUnavailable: 0
          template:
            metadata:
              labels:
                {{- include "kubernator.labels" . | nindent 8 }}
            spec:
              automountServiceAccountToken: false
              securityContext:
                {{- toYaml .Values.securityContext | nindent 8 }}
              containers:
                - name: {{ .Chart.Name }}
                  image: "{{ .Values.image.repository }}:{{ .Values.image.tag | default .Chart.AppVersion }}"
                  imagePullPolicy: {{ .Values.image.pullPolicy }}
                  securityContext:
                    {{- toYaml .Values.containerSecurityContext | nindent 12 }}
                  {{- if .Values.service.port }}
                  ports:
                    - name: http
                      containerPort: {{ .Values.service.port }}
                      protocol: TCP
                  {{- end }}
                  {{- with .Values.env }}
                  env:
                    {{- range $k, $v := . }}
                    - name: {{ $k }}
                      value: {{ $v | quote }}
                    {{- end }}
                  {{- end }}
                  resources:
                    {{- toYaml .Values.resources | nindent 12 }}
                  {{- if .Values.healthProbe.enabled }}
                  livenessProbe:
                    httpGet:
                      path: {{ .Values.healthProbe.path }}
                      port: {{ .Values.service.port }}
                    initialDelaySeconds: 15
                    periodSeconds: 20
                  readinessProbe:
                    httpGet:
                      path: {{ .Values.healthProbe.path }}
                      port: {{ .Values.service.port }}
                    initialDelaySeconds: 5
                    periodSeconds: 10
                  {{- end }}
                  volumeMounts:
                    - name: tmp
                      mountPath: /tmp
              volumes:
                - name: tmp
                  emptyDir:
                    medium: Memory
                    sizeLimit: 64Mi
        """;

    public const string Service = """
        {{- if .Values.service.enabled }}
        apiVersion: v1
        kind: Service
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          type: {{ .Values.service.type }}
          selector:
            {{- include "kubernator.selectorLabels" . | nindent 4 }}
          ports:
            - name: http
              port: {{ .Values.service.port }}
              targetPort: {{ .Values.service.port }}
              protocol: TCP
        {{- end }}
        """;

    public const string Ingress = """
        {{- if .Values.ingress.enabled }}
        apiVersion: networking.k8s.io/v1
        kind: Ingress
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
          {{- with .Values.ingress.annotations }}
          annotations:
            {{- toYaml . | nindent 4 }}
          {{- end }}
        spec:
          ingressClassName: {{ .Values.ingress.className }}
          {{- with .Values.ingress.tls }}
          tls:
            {{- toYaml . | nindent 4 }}
          {{- end }}
          rules:
            {{- range .Values.ingress.hosts }}
            - host: {{ .host | quote }}
              http:
                paths:
                  {{- range .paths }}
                  - path: {{ .path }}
                    pathType: {{ .pathType }}
                    backend:
                      service:
                        name: {{ include "kubernator.fullname" $ }}
                        port:
                          number: {{ $.Values.service.port }}
                  {{- end }}
            {{- end }}
        {{- end }}
        """;

    public const string Hpa = """
        {{- if .Values.autoscaling.enabled }}
        apiVersion: autoscaling/v2
        kind: HorizontalPodAutoscaler
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          scaleTargetRef:
            apiVersion: apps/v1
            kind: Deployment
            name: {{ include "kubernator.fullname" . }}
          minReplicas: {{ .Values.autoscaling.minReplicas }}
          maxReplicas: {{ .Values.autoscaling.maxReplicas }}
          metrics:
            {{- if .Values.autoscaling.targetCPUUtilizationPercentage }}
            - type: Resource
              resource:
                name: cpu
                target:
                  type: Utilization
                  averageUtilization: {{ .Values.autoscaling.targetCPUUtilizationPercentage }}
            {{- end }}
            {{- if .Values.autoscaling.targetMemoryUtilizationPercentage }}
            - type: Resource
              resource:
                name: memory
                target:
                  type: Utilization
                  averageUtilization: {{ .Values.autoscaling.targetMemoryUtilizationPercentage }}
            {{- end }}
        {{- end }}
        """;

    public const string Pdb = """
        {{- if .Values.podDisruptionBudget.enabled }}
        apiVersion: policy/v1
        kind: PodDisruptionBudget
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          selector:
            matchLabels:
              {{- include "kubernator.selectorLabels" . | nindent 6 }}
          {{- if .Values.podDisruptionBudget.minAvailable }}
          minAvailable: {{ .Values.podDisruptionBudget.minAvailable }}
          {{- else if .Values.podDisruptionBudget.maxUnavailable }}
          maxUnavailable: {{ .Values.podDisruptionBudget.maxUnavailable }}
          {{- end }}
        {{- end }}
        """;

    public const string NetworkPolicy = """
        {{- if .Values.networkPolicy.enabled }}
        apiVersion: networking.k8s.io/v1
        kind: NetworkPolicy
        metadata:
          name: {{ include "kubernator.fullname" . }}-default-deny
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          podSelector:
            matchLabels:
              {{- include "kubernator.selectorLabels" . | nindent 6 }}
          policyTypes:
            - Ingress
            - Egress
          egress:
            - to:
                - namespaceSelector:
                    matchLabels:
                      kubernetes.io/metadata.name: kube-system
                  podSelector:
                    matchLabels:
                      k8s-app: kube-dns
              ports:
                - protocol: UDP
                  port: 53
                - protocol: TCP
                  port: 53
          {{- if .Values.service.port }}
          ingress:
            - ports:
                - protocol: TCP
                  port: {{ .Values.service.port }}
          {{- end }}
        {{- end }}
        """;

    public const string TlsSecret = """
        {{- if and .Values.ingress.enabled .Values.ingress.tlsSecret.create }}
        apiVersion: v1
        kind: Secret
        type: kubernetes.io/tls
        metadata:
          name: {{ .Values.ingress.tlsSecret.name }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        data:
          tls.crt: {{ required "ingress.tlsSecret.cert is required when create=true" .Values.ingress.tlsSecret.cert | b64enc }}
          tls.key: {{ required "ingress.tlsSecret.key is required when create=true" .Values.ingress.tlsSecret.key | b64enc }}
        {{- end }}
        """;

    public const string CertManagerCertificate = """
        {{- if .Values.certManager.enabled }}
        apiVersion: cert-manager.io/v1
        kind: Certificate
        metadata:
          name: {{ include "kubernator.fullname" . }}
          namespace: {{ .Release.Namespace }}
          labels:
            {{- include "kubernator.labels" . | nindent 4 }}
        spec:
          secretName: {{ .Values.ingress.tlsSecret.name }}
          dnsNames:
            {{- range .Values.certManager.dnsNames }}
            - {{ . | quote }}
            {{- end }}
          issuerRef:
            kind: {{ .Values.certManager.issuer.kind }}
            name: {{ .Values.certManager.issuer.name }}
            group: cert-manager.io
        {{- end }}
        """;
}
