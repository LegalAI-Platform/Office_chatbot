# Utility image for one-off jobs: preflight, storage-init, healthcheck,
# backup, restore, gen-secrets. Keeps host scripts thin and identical on
# Windows and Linux.
FROM alpine:3.22.6

RUN apk add --no-cache \
      bash coreutils curl jq openssl tzdata \
      postgresql16-client \
      redis \
      aws-cli

ENV TZ=Africa/Cairo \
    AWS_REQUEST_CHECKSUM_CALCULATION=when_required \
    AWS_RESPONSE_CHECKSUM_VALIDATION=when_required \
    AWS_EC2_METADATA_DISABLED=true

COPY bin/ /usr/local/bin/
RUN chmod 0755 /usr/local/bin/*

CMD ["healthcheck"]
