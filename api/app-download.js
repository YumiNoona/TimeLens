import {
  applicationAssetName,
  assetRedirectUrl,
  getLatestRelease,
} from '../server/github-release.js';

export default async function handler(request, response) {
  if (request.method !== 'GET' && request.method !== 'HEAD') {
    response.setHeader('Allow', 'GET, HEAD');
    return response.status(405).json({ error: 'Method not allowed' });
  }

  try {
    const { asset } = await getLatestRelease(applicationAssetName());
    const downloadUrl = await assetRedirectUrl(asset);
    response.setHeader('Cache-Control', 'private, no-store');
    response.setHeader('Location', downloadUrl);
    return response.status(302).end();
  } catch (error) {
    console.error('Application update download failed:', error);
    response.setHeader('Cache-Control', 'no-store');
    return response.status(503).json({ error: 'The TimeLens update is temporarily unavailable.' });
  }
}
