import {
  applicationAssetName,
  getLatestRelease,
  getReleaseChecksum,
  publicDownloadUrl,
  releaseVersion,
} from '../server/github-release.js';

export default async function handler(request, response) {
  if (request.method !== 'GET') {
    response.setHeader('Allow', 'GET');
    return response.status(405).json({ error: 'Method not allowed' });
  }

  try {
    const { release, asset: executable } = await getLatestRelease(applicationAssetName());
    const sha256 = await getReleaseChecksum(release, executable.name);
    response.setHeader('Cache-Control', 'public, s-maxage=60, stale-while-revalidate=300');
    return response.status(200).json({
      version: releaseVersion(release.tag_name),
      publishedAt: release.published_at,
      releaseNotes: String(release.body || '').slice(0, 16000),
      size: executable.size,
      sha256,
      downloadUrl: publicDownloadUrl(),
    });
  } catch (error) {
    console.error('Latest release lookup failed:', error);
    response.setHeader('Cache-Control', 'no-store');
    return response.status(503).json({ error: 'The latest TimeLens release is temporarily unavailable.' });
  }
}
