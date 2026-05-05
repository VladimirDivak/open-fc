using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HTraceAO.Scripts.Services.MotionVectorService
{
	public class HTraceMotionVector : MonoBehaviour
	{
		private void Start()
		{
			if (MotionVectorService.Instance != null)
			{
				List<Renderer> renderers = this.gameObject.GetComponentsInChildren<Renderer>().ToList();
				if (this.gameObject.GetComponent<Renderer>() != null)
					renderers.Add(this.gameObject.GetComponent<Renderer>());
				
				MotionVectorService.Instance.AddObject(gameObject, renderers);
			}
		}

		private void OnDestroy()
		{
			if (MotionVectorService.Instance != null)
			{
				MotionVectorService.Instance.RemoveObject(gameObject);
			}
		}
	}
}
