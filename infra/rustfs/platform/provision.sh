#!/bin/sh
# Creates the buckets, their access rules, and the per-host storage users. Safe to run repeatedly.
set -eu

: "${STORAGE_URL:?STORAGE_URL is required}"
: "${ROOT_ACCESS_KEY:?ROOT_ACCESS_KEY is required}"
: "${ROOT_SECRET_KEY:?ROOT_SECRET_KEY is required}"
: "${PRIVATE_BUCKET:?PRIVATE_BUCKET is required}"
: "${PUBLIC_BUCKET:?PUBLIC_BUCKET is required}"
: "${WEB_ORIGIN:?WEB_ORIGIN is required}"
: "${UPLOAD_EXPIRY_DAYS:?UPLOAD_EXPIRY_DAYS is required}"
: "${API_FILES_SECRET_KEY:?API_FILES_SECRET_KEY is required}"
: "${MEDIA_WORKER_SECRET_KEY:?MEDIA_WORKER_SECRET_KEY is required}"

work=$(mktemp -d)
render() {
  sed -e "s|\${PRIVATE_BUCKET}|$PRIVATE_BUCKET|g" \
      -e "s|\${PUBLIC_BUCKET}|$PUBLIC_BUCKET|g" \
      -e "s|\${WEB_ORIGIN}|$WEB_ORIGIN|g" \
      -e "s|\${UPLOAD_EXPIRY_DAYS}|$UPLOAD_EXPIRY_DAYS|g" \
      "/provisioning/$1" > "$work/$1"
}
for file in public-read-policy.json api-files-policy.json media-worker-policy.json upload-lifecycle.json upload-cors.xml; do
  render "$file"
done

echo "Provisioning object storage at $STORAGE_URL"
rc --quiet alias set storage "$STORAGE_URL" "$ROOT_ACCESS_KEY" "$ROOT_SECRET_KEY" --bucket-lookup path
echo "Ensuring buckets $PRIVATE_BUCKET and $PUBLIC_BUCKET"
rc --quiet bucket create --ignore-existing "storage/$PRIVATE_BUCKET"
rc --quiet bucket create --ignore-existing "storage/$PUBLIC_BUCKET"

# Published media is readable by key, without listing. Private objects are reachable only through signed URLs.
echo "Applying public read, upload CORS for $WEB_ORIGIN, and $UPLOAD_EXPIRY_DAYS-day upload expiry"
rc --quiet bucket anonymous set-json "$work/public-read-policy.json" "storage/$PUBLIC_BUCKET"
rc --quiet bucket cors set "storage/$PRIVATE_BUCKET" "$work/upload-cors.xml"

# Importing replaces the bucket's lifecycle configuration, so repeated runs keep one rule.
rc --quiet bucket lifecycle rule import "storage/$PRIVATE_BUCKET" "$work/upload-lifecycle.json"

provision_user() {
  echo "Provisioning storage user $1"
  rc --quiet admin user add storage "$1" "$2"
  rc --quiet admin policy create storage "$1" "$work/$1-policy.json"
  rc --quiet admin policy attach storage "$1" --user "$1"
}
provision_user api-files "$API_FILES_SECRET_KEY"
provision_user media-worker "$MEDIA_WORKER_SECRET_KEY"
echo "Object storage provisioned"
